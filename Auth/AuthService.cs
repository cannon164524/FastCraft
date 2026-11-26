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
        // Public Xbox Live client id, used by several open-source launchers. If Microsoft
        // retires it, register your own app in Azure and replace this value.
        public const string ClientId = "00000000402b5328";

        private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
        private const string McEntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";
        private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

        public async Task<DeviceCode> StartDeviceCodeAsync(CancellationToken cancellationToken)
        {
            var form = "client_id=" + Uri.EscapeDataString(ClientId)
                     + "&scope=" + Uri.EscapeDataString("XboxLive.signin offline_access");
            var result = await Http.PostFormAsync(DeviceCodeUrl, form, cancellationToken).ConfigureAwait(false);
            if (result.Status != HttpStatusCode.OK)
            {
                throw new AuthException("Microsoft sign-in could not be started (HTTP " + (int)result.Status + ").");
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
                throw new AuthException(DescribeXstsError(xerr));
            }
            var xuid = uhs;

            // 3. Minecraft services
            var loginBody = new JsonObject { ["identityToken"] = "XBL3.0 x=" + uhs + ";" + xstsToken };
            var loginResult = await Http.PostJsonAsync(McLoginUrl, loginBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
            string mcToken = null;
            long expiresIn = 0;
            using (var document = JsonDocument.Parse(loginResult.Body))
            {
                mcToken = JsonHelp.GetString(document.RootElement, "access_token");
                expiresIn = JsonHelp.GetLong(document.RootElement, "expires_in");
            }
            if (string.IsNullOrEmpty(mcToken))
            {
                throw new AuthException("Minecraft sign-in failed. " + Trimmed(loginResult.Body));
            }

            // 4. Ownership check — this launcher does not do offline mode.
            var entitlements = await Http.GetAsync(McEntitlementsUrl, "Bearer " + mcToken, cancellationToken).ConfigureAwait(false);
            if (entitlements.Status != HttpStatusCode.OK)
            {
                throw new AuthException("Could not check game ownership (HTTP " + (int)entitlements.Status + ").");
            }
            bool owns = false;
            using (var document = JsonDocument.Parse(entitlements.Body))
            {
                JsonElement items;
                if (document.RootElement.TryGetProperty("items", out items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var name = JsonHelp.GetString(item, "name") ?? "";
                        if (name.IndexOf("minecraft", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            owns = true;
                            break;
                        }
                    }
                }
            }
            if (!owns)
            {
                throw new AuthException("This Microsoft account does not own Minecraft: Java Edition. FastCraft only works with a legitimately purchased copy.");
            }

            // 5. Profile
            var profile = await Http.GetAsync(McProfileUrl, "Bearer " + mcToken, cancellationToken).ConfigureAwait(false);
            if (profile.Status == HttpStatusCode.NotFound)
            {
                throw new AuthException("This account has no Minecraft: Java Edition profile. Buy the game at minecraft.net first.");
            }
            if (profile.Status != HttpStatusCode.OK)
            {
                throw new AuthException("Could not load the Minecraft profile (HTTP " + (int)profile.Status + ").");
            }

            var account = new Account
            {
                AccessToken = mcToken,
                RefreshToken = token.RefreshToken,
                Xuid = xuid,
                ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 86400)
            };
            using (var document = JsonDocument.Parse(profile.Body))
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
                     + "&scope=" + Uri.EscapeDataString("XboxLive.signin offline_access");
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
