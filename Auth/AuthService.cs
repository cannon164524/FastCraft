using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FastCraft.Core;

namespace FastCraft.Auth
{
    public sealed class DeviceCode
    {
        public string DeviceCodeValue;
        public string UserCode;
        public string VerificationUri;
        public int IntervalSeconds;
        public DateTime ExpiresAt;
    }

    public sealed class TokenResult
    {
        public string AccessToken;
        public string RefreshToken;
    }

    /// <summary>
    /// Microsoft sign-in (device code flow) followed by the Minecraft services exchange:
    /// MSA -> Xbox Live -> XSTS -> Minecraft access token -> ownership check.
    /// Only accounts that actually own Minecraft: Java Edition get through.
    /// </summary>
    public sealed class AuthService
    {
        // Public Azure app id for the Microsoft Accounts tenant, with the device code flow
        // enabled (used by Prism Launcher). The old Xbox Live id 00000000402b5328 was
        // retired and now returns AADSTS700016 from the consumers endpoint.
        public const string ClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";

        private const string NoProfileMessage = "This Microsoft account has no Minecraft: Java Edition profile, so it does not own the game. Buy Minecraft: Java Edition at minecraft.net, or start the official launcher once if you play through Xbox Game Pass.";

        private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string McLauncherLoginUrl = "https://api.minecraftservices.com/launcher/login";
        private const string McLoginWithXboxUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
        private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

        public async Task<DeviceCode> StartDeviceCodeAsync(CancellationToken cancellationToken)
        {
            var form = "client_id=" + Uri.EscapeDataString(ClientId)
                     + "&scope=" + Uri.EscapeDataString("XboxLive.SignIn XboxLive.offline_access");
            var result = await Http.PostFormAsync(DeviceCodeUrl, form, cancellationToken).ConfigureAwait(false);
            AuthLog.Write("Device code: HTTP " + (int)result.Status);
            if (result.Status != HttpStatusCode.OK)
            {
                throw new AuthException("Microsoft sign-in could not be started (HTTP " + (int)result.Status + "). "
                    + DescribeError(result.Body));
            }

            var code = new DeviceCode();
            using (var document = JsonDocument.Parse(result.Body))
            {
                var root = document.RootElement;
                code.DeviceCodeValue = JsonHelp.GetString(root, "device_code");
                code.UserCode = JsonHelp.GetString(root, "user_code");
                code.VerificationUri = JsonHelp.GetString(root, "verification_uri");
                code.IntervalSeconds = (int)JsonHelp.GetLong(root, "interval");
                var expires = JsonHelp.GetLong(root, "expires_in");
                code.ExpiresAt = DateTime.UtcNow.AddSeconds(expires > 0 ? expires : 900);
            }

            if (string.IsNullOrEmpty(code.DeviceCodeValue) || string.IsNullOrEmpty(code.UserCode))
            {
                throw new AuthException("Microsoft returned an incomplete device code.");
            }
            return code;
        }

        public async Task<TokenResult> PollAsync(DeviceCode code, CancellationToken cancellationToken)
        {
            int interval = Math.Max(3, code.IntervalSeconds);
            while (DateTime.UtcNow < code.ExpiresAt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(interval * 1000, cancellationToken).ConfigureAwait(false);

                var form = "grant_type=" + Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:device_code")
                         + "&client_id=" + Uri.EscapeDataString(ClientId)
                         + "&device_code=" + Uri.EscapeDataString(code.DeviceCodeValue);
                var result = await Http.PostFormAsync(TokenUrl, form, cancellationToken).ConfigureAwait(false);

                if (result.Status == HttpStatusCode.OK)
                {
                    return ParseToken(result.Body);
                }

                var error = TryGetError(result.Body);
                if (error == "authorization_pending")
                {
                    continue;
                }
                if (error == "slow_down")
                {
                    interval += 2;
                    continue;
                }
                if (error == "expired_token")
                {
                    throw new AuthException("The sign-in code expired. Please try again.");
                }
                if (error == "authorization_declined")
                {
                    throw new AuthException("The sign-in was declined.");
                }
                if (error == "bad_verification_code")
                {
                    throw new AuthException("The sign-in code was not valid.");
                }
                throw new AuthException("Microsoft sign-in failed: " + error);
            }
            throw new AuthException("The sign-in code expired. Please try again.");
        }

        public async Task<Account> CompleteLoginAsync(TokenResult token, CancellationToken cancellationToken)
        {
            // 1. Xbox Live
            var xblBody = new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["AuthMethod"] = "RPS",
                    ["SiteName"] = "user.auth.xboxlive.com",
                    ["RpsTicket"] = "d=" + token.AccessToken
                },
                ["RelyingParty"] = "http://auth.xboxlive.com",
                ["TokenType"] = "JWT"
            };
            var xblResult = await Http.PostJsonAsync(XblUrl, xblBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
            AuthLog.Write("Xbox Live: HTTP " + (int)xblResult.Status);
            if (xblResult.Status != HttpStatusCode.OK)
            {
                throw new AuthException("Xbox Live sign-in failed (HTTP " + (int)xblResult.Status + ").");
            }
            string xblToken;
            using (var document = JsonDocument.Parse(xblResult.Body))
            {
                xblToken = JsonHelp.GetString(document.RootElement, "Token");
            }
            if (string.IsNullOrEmpty(xblToken))
            {
                throw new AuthException("Xbox Live returned no token.");
            }

            // 2. XSTS
            var userTokens = new JsonArray();
            userTokens.Add(xblToken);
            var xstsBody = new JsonObject
            {
                ["Properties"] = new JsonObject
                {
                    ["SandboxId"] = "RETAIL",
                    ["UserTokens"] = userTokens
                },
                ["RelyingParty"] = "rp://api.minecraftservices.com/",
                ["TokenType"] = "JWT"
            };
            var xstsResult = await Http.PostJsonAsync(XstsUrl, xstsBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
            string xstsToken = null;
            string uhs = null;
            long xerr = 0;
            using (var document = JsonDocument.Parse(xstsResult.Body))
            {
                var root = document.RootElement;
                xstsToken = JsonHelp.GetString(root, "Token");
                xerr = JsonHelp.GetLong(root, "XErr");
                JsonElement claims;
                if (root.TryGetProperty("DisplayClaims", out claims) && claims.ValueKind == JsonValueKind.Object)
                {
                    JsonElement xui;
                    if (claims.TryGetProperty("xui", out xui) && xui.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in xui.EnumerateArray())
                        {
                            var claim = JsonHelp.GetString(item, "uhs");
                            if (!string.IsNullOrEmpty(claim))
                            {
                                uhs = claim;
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(xstsToken) || string.IsNullOrEmpty(uhs))
            {
                AuthLog.Write("XSTS: HTTP " + (int)xstsResult.Status + ", XErr=" + xerr);
                throw new AuthException(DescribeXstsError(xerr));
            }
            AuthLog.Write("XSTS: HTTP " + (int)xstsResult.Status + ", XErr=0, uhs length " + uhs.Length);
            var xuid = uhs;

            // 3. Minecraft access token and profile. The official launcher's /launcher/login
            // is tried first, then the older login_with_xbox endpoint; the first token whose
            // profile loads is used, so a route that hands back a rejected token cannot
            // block sign-in.
            var xToken = "XBL3.0 x=" + uhs + ";" + xstsToken;
            string mcToken = null;
            long expiresIn = 0;
            string profileBody = null;
            string failure = "";

            var launcherBody = new JsonObject { ["xtoken"] = xToken, ["platform"] = "PC_LAUNCHER" };
            var launcherResult = await Http.PostJsonAsync(McLauncherLoginUrl, launcherBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
            AuthLog.Write("Launcher login: HTTP " + (int)launcherResult.Status);

            var launcherToken = ReadMinecraftToken(launcherResult, out expiresIn);
            if (string.IsNullOrEmpty(launcherToken))
            {
                failure = "Launcher login HTTP " + (int)launcherResult.Status + ": " + DescribeError(launcherResult.Body);
            }
            else
            {
                var profile = await Http.GetAsync(McProfileUrl, "Bearer " + launcherToken, cancellationToken).ConfigureAwait(false);
                AuthLog.Write("Profile (launcher login token): HTTP " + (int)profile.Status + " " + DescribeError(profile.Body));
                if (profile.Status == HttpStatusCode.OK)
                {
                    mcToken = launcherToken;
                    profileBody = profile.Body;
                }
                else if (profile.Status == HttpStatusCode.NotFound)
                {
                    throw new AuthException(NoProfileMessage);
                }
                else
                {
                    failure = "Profile HTTP " + (int)profile.Status + ": " + DescribeError(profile.Body);
                }
            }

            if (string.IsNullOrEmpty(profileBody))
            {
                var loginBody = new JsonObject { ["identityToken"] = xToken };
                var loginResult = await Http.PostJsonAsync(McLoginWithXboxUrl, loginBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
                AuthLog.Write("login_with_xbox: HTTP " + (int)loginResult.Status);

                long xboxExpires;
                var xboxToken = ReadMinecraftToken(loginResult, out xboxExpires);
                if (string.IsNullOrEmpty(xboxToken))
                {
                    failure = failure + " / login_with_xbox HTTP " + (int)loginResult.Status + ": " + DescribeError(loginResult.Body);
                }
                else
                {
                    var profile = await Http.GetAsync(McProfileUrl, "Bearer " + xboxToken, cancellationToken).ConfigureAwait(false);
                    AuthLog.Write("Profile (login_with_xbox token): HTTP " + (int)profile.Status + " " + DescribeError(profile.Body));
                    if (profile.Status == HttpStatusCode.OK)
                    {
                        mcToken = xboxToken;
                        expiresIn = xboxExpires;
                        profileBody = profile.Body;
                    }
                    else if (profile.Status == HttpStatusCode.NotFound)
                    {
                        throw new AuthException(NoProfileMessage);
                    }
                    else
                    {
                        failure = failure + " / Profile HTTP " + (int)profile.Status + ": " + DescribeError(profile.Body);
                    }
                }
            }

            if (string.IsNullOrEmpty(profileBody) || string.IsNullOrEmpty(mcToken))
            {
                throw new AuthException("Could not sign in to Minecraft. " + failure);
            }

            // 4. Ownership: a profile exists only for an account with Minecraft: Java
            // Edition (or Xbox Game Pass), and /minecraft/profile answers 404 otherwise.
            // The old /entitlements/mcstore check now answers 401, so it is not used.
            var account = new Account
            {
                AccessToken = mcToken,
                RefreshToken = token.RefreshToken,
                Xuid = xuid,
                ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 86400)
            };
            using (var document = JsonDocument.Parse(profileBody))
            {
                account.Name = JsonHelp.GetString(document.RootElement, "name");
                account.Uuid = JsonHelp.GetString(document.RootElement, "id");
            }
            if (string.IsNullOrEmpty(account.Name) || string.IsNullOrEmpty(account.Uuid))
            {
                throw new AuthException("Minecraft returned an incomplete profile.");
            }
            return account;
        }

        public async Task<Account> RefreshAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null || string.IsNullOrEmpty(account.RefreshToken))
            {
                throw new AuthException("No saved Microsoft session. Please sign in again.");
            }

            var form = "grant_type=refresh_token"
                     + "&client_id=" + Uri.EscapeDataString(ClientId)
                     + "&refresh_token=" + Uri.EscapeDataString(account.RefreshToken)
                     + "&scope=" + Uri.EscapeDataString("XboxLive.SignIn XboxLive.offline_access");
            var result = await Http.PostFormAsync(TokenUrl, form, cancellationToken).ConfigureAwait(false);

            string access = null;
            string refresh = null;
            try
            {
                using (var document = JsonDocument.Parse(result.Body))
                {
                    if (result.Status == HttpStatusCode.OK)
                    {
                        access = JsonHelp.GetString(document.RootElement, "access_token");
                        refresh = JsonHelp.GetString(document.RootElement, "refresh_token");
                    }
                }
            }
            catch (JsonException)
            {
            }

            if (string.IsNullOrEmpty(access))
            {
                var error = TryGetError(result.Body);
                if (error == "invalid_grant")
                {
                    throw new AuthException("Your Microsoft session expired. Please sign in again.");
                }
                throw new AuthException("Could not refresh the Microsoft session: " + error);
            }

            var token = new TokenResult
            {
                AccessToken = access,
                RefreshToken = string.IsNullOrEmpty(refresh) ? account.RefreshToken : refresh
            };
            return await CompleteLoginAsync(token, cancellationToken).ConfigureAwait(false);
        }

        private static TokenResult ParseToken(string body)
        {
            var token = new TokenResult();
            using (var document = JsonDocument.Parse(body))
            {
                token.AccessToken = JsonHelp.GetString(document.RootElement, "access_token");
                token.RefreshToken = JsonHelp.GetString(document.RootElement, "refresh_token");
            }
            if (string.IsNullOrEmpty(token.AccessToken))
            {
                throw new AuthException("Microsoft returned no access token.");
            }
            return token;
        }

        private static string TryGetError(string body)
        {
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var error = JsonHelp.GetString(document.RootElement, "error");
                    if (!string.IsNullOrEmpty(error)) return error;
                    var description = JsonHelp.GetString(document.RootElement, "error_description");
                    if (!string.IsNullOrEmpty(description)) return description;
                }
            }
            catch (JsonException)
            {
            }
            return "unknown error";
        }

        private static string ReadMinecraftToken((HttpStatusCode Status, string Body) result, out long expiresIn)
        {
            expiresIn = 0;
            if (result.Status != HttpStatusCode.OK) return null;
            try
            {
                using (var document = JsonDocument.Parse(result.Body))
                {
                    expiresIn = JsonHelp.GetLong(document.RootElement, "expires_in");
                    return JsonHelp.GetString(document.RootElement, "access_token");
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string DescribeError(string body)        {
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var error = JsonHelp.GetString(document.RootElement, "error");
                    var description = JsonHelp.GetString(document.RootElement, "error_description");
                    var parts = "";
                    if (!string.IsNullOrEmpty(error)) parts = error;
                    if (!string.IsNullOrEmpty(description)) parts = string.IsNullOrEmpty(parts) ? description : parts + ": " + description;
                    if (parts.Length > 400) parts = parts.Substring(0, 400);
                    return parts;
                }
            }
            catch (JsonException)
            {
            }
            return "";
        }

        private static string DescribeXstsError(long xerr)
        {
            switch (xerr)
            {
                case 2148916233:
                    return "This Microsoft account has no Xbox profile yet. Sign in once at xbox.com, then try again.";
                case 2148916235:
                    return "Xbox Live is not available in this account's country.";
                case 2148916236:
                    return "This account needs adult verification (South Korea).";
                case 2148916237:
                    return "This account needs adult verification (South Korea).";
                case 2148916238:
                    return "This is a child account. Ask a parent to add it to a Microsoft family, then try again.";
                default:
                    return "Xbox Live sign-in failed (code " + xerr + ").";
            }
        }

        private static string Trimmed(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            return body.Length > 300 ? body.Substring(0, 300) : body;
        }
    }
}
