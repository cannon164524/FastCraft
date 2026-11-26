using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    ///
    /// Mojang rate-limits the last steps hard (HTTP 429) and every extra attempt extends the
    /// wait, so each run sends as few requests as it can: one launcher-login token request, one
    /// quiet retry of the profile, and the older login_with_xbox route only as a fallback. A 429
    /// anywhere ends the run and is remembered in AuthBackoff.
    /// </summary>
    public sealed class AuthService
    {
        // Public Azure app id for the Microsoft Accounts tenant, with the device code flow
        // enabled (used by Prism Launcher). The old Xbox Live id 00000000402b5328 was
        // retired and now returns AADSTS700016 from the consumers endpoint.
        public const string ClientId = "c36a9fb6-4f2a-41ff-90bd-ae7cc92031eb";

        private readonly string _clientId;

        public AuthService()
        {
            _clientId = LoadClientId();
        }

        /// <summary>True when the user supplied an own Azure app id instead of the shared one.</summary>
        public bool UsesCustomClientId { get { return _clientId != ClientId; } }

        private static string ClientIdFilePath { get { return Path.Combine(Locations.Root, "client-id.txt"); } }

        /// <summary>
        /// An optional own Azure app id, one GUID on one line in
        /// %APPDATA%\FastCraft\client-id.txt. The shared id has a shared request budget at
        /// Mojang, which can be used up by other people; an own id does not have that problem.
        /// </summary>
        private static string LoadClientId()
        {
            try
            {
                if (File.Exists(ClientIdFilePath))
                {
                    // The whole file is searched, so a line pasted from the portal such as
                    // "Application (client) ID: …" works as well as the bare GUID.
                    var text = File.ReadAllText(ClientIdFilePath);
                    var match = GuidPattern.Match(text);
                    Guid id;
                    if (match.Success && Guid.TryParse(match.Value, out id)) return id.ToString();
                    AuthLog.Write("client-id.txt holds no GUID, so the built-in app id is used.");
                }
            }
            catch
            {
            }
            return ClientId;
        }

        private static readonly Regex GuidPattern = new Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        private const string NoProfileMessage = "This Microsoft account has no Minecraft: Java Edition profile, so it does not own the game. Buy Minecraft: Java Edition at minecraft.net, or start the official launcher once if you play through Xbox Game Pass.";

        private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string McLauncherLoginUrl = "https://api.minecraftservices.com/launcher/login";
        private const string McLoginWithXboxUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
        private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
        private const string McEntitlementsUrl = "https://api.minecraftservices.com/entitlements/mcstore";

        public async Task<DeviceCode> StartDeviceCodeAsync(CancellationToken cancellationToken)
        {
            ThrowIfThrottled();
            AuthLog.Write("Client id: " + _clientId);

            var form = "client_id=" + Uri.EscapeDataString(_clientId)
                     + "&scope=" + Uri.EscapeDataString("XboxLive.SignIn XboxLive.offline_access");
            var result = await Http.PostFormAsync(DeviceCodeUrl, form, cancellationToken).ConfigureAwait(false);
            AuthLog.Write("Device code: HTTP " + result.Code);
            if (result.IsRateLimited) throw Throttled(result, "Device code");
            if (!result.IsOk)
            {
                throw new AuthException("Microsoft sign-in could not be started (HTTP " + result.Code + "). "
                    + DescribeError(result.Body) + HintsFor(result.Body));
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
                         + "&client_id=" + Uri.EscapeDataString(_clientId)
                         + "&device_code=" + Uri.EscapeDataString(code.DeviceCodeValue);
                var result = await Http.PostFormAsync(TokenUrl, form, cancellationToken).ConfigureAwait(false);

                if (result.IsRateLimited) throw Throttled(result, "Token poll");
                if (result.IsOk)
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
            ThrowIfThrottled();

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
            AuthLog.Write("Xbox Live: HTTP " + xblResult.Code);
            if (xblResult.IsRateLimited) throw Throttled(xblResult, "Xbox Live");
            if (!xblResult.IsOk)
            {
                throw new AuthException("Xbox Live sign-in failed (HTTP " + xblResult.Code + "). " + DescribeError(xblResult.Body));
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
            if (xstsResult.IsRateLimited) throw Throttled(xstsResult, "XSTS");

            string xstsToken = null;
            string uhs = null;
            string xuid = null;
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
                            if (string.IsNullOrEmpty(uhs)) uhs = JsonHelp.GetString(item, "uhs");
                            if (string.IsNullOrEmpty(xuid)) xuid = JsonHelp.GetString(item, "xid");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(xstsToken) || string.IsNullOrEmpty(uhs))
            {
                AuthLog.Write("XSTS: HTTP " + xstsResult.Code + ", XErr=" + xerr);
                throw new AuthException(DescribeXstsError(xerr));
            }
            // The user hash is not the account id: --xuid wants the xid claim, and the user hash
            // is only the fallback when the claim is missing.
            if (string.IsNullOrEmpty(xuid)) xuid = uhs;
            AuthLog.Write("XSTS: HTTP " + xstsResult.Code + ", XErr=0, uhs length " + uhs.Length);

            // 3. Minecraft access token and profile. The official launcher's /launcher/login is
            // the route Mojang's own launcher and Prism use and is tried once; the older
            // login_with_xbox endpoint is heavily rate limited and only runs as a fallback.
            var xToken = "XBL3.0 x=" + uhs + ";" + xstsToken;
            string mcToken = null;
            string lastToken = null;
            long expiresIn = 0;
            string profileBody = null;
            var failures = new List<string>();

            var launcherBody = new JsonObject { ["xtoken"] = xToken, ["platform"] = "PC_LAUNCHER" };
            var launcherResult = await Http.PostJsonAsync(McLauncherLoginUrl, launcherBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
            AuthLog.Write("Launcher login: HTTP " + launcherResult.Code);
            if (launcherResult.IsRateLimited) throw Throttled(launcherResult, "Launcher login");

            var launcherToken = ReadMinecraftToken(launcherResult, out expiresIn);
            if (!string.IsNullOrEmpty(launcherToken)) lastToken = launcherToken;
            if (string.IsNullOrEmpty(launcherToken))
            {
                failures.Add("Launcher login HTTP " + launcherResult.Code + ": " + DescribeError(launcherResult.Body) + launcherResult.Diagnostics);
            }
            else
            {
                var profile = await Http.GetAsync(McProfileUrl, "Bearer " + launcherToken, cancellationToken).ConfigureAwait(false);
                AuthLog.Write("Profile (launcher login token): HTTP " + profile.Code + " " + DescribeError(profile.Body) + profile.Diagnostics);
                if (profile.IsOk)
                {
                    mcToken = launcherToken;
                    profileBody = profile.Body;
                }
                else if (profile.Status == HttpStatusCode.NotFound)
                {
                    throw new AuthException(NoProfileMessage);
                }
                else if (profile.IsRateLimited)
                {
                    throw Throttled(profile, "Profile");
                }
                else
                {
                    // A token that was just issued is occasionally refused once while it
                    // propagates, so the profile is asked for one more time before the
                    // fallback route is used.
                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                    var retry = await Http.GetAsync(McProfileUrl, "Bearer " + launcherToken, cancellationToken).ConfigureAwait(false);
                    AuthLog.Write("Profile retry (launcher login token): HTTP " + retry.Code + " " + DescribeError(retry.Body) + retry.Diagnostics);
                    if (retry.IsOk)
                    {
                        mcToken = launcherToken;
                        profileBody = retry.Body;
                    }
                    else if (retry.Status == HttpStatusCode.NotFound)
                    {
                        throw new AuthException(NoProfileMessage);
                    }
                    else if (retry.IsRateLimited)
                    {
                        throw Throttled(retry, "Profile");
                    }
                    else
                    {
                        failures.Add("Profile HTTP " + retry.Code + ": " + DescribeError(retry.Body) + retry.Diagnostics);
                    }
                }
            }

            if (string.IsNullOrEmpty(profileBody))
            {
                ThrowIfThrottled();

                var loginBody = new JsonObject { ["identityToken"] = xToken };
                var loginResult = await Http.PostJsonAsync(McLoginWithXboxUrl, loginBody.ToJsonString(), cancellationToken).ConfigureAwait(false);
                AuthLog.Write("login_with_xbox: HTTP " + loginResult.Code);
                if (loginResult.IsRateLimited) throw Throttled(loginResult, "login_with_xbox");

                long xboxExpires;
                var xboxToken = ReadMinecraftToken(loginResult, out xboxExpires);
                if (!string.IsNullOrEmpty(xboxToken)) lastToken = xboxToken;
                if (string.IsNullOrEmpty(xboxToken))
                {
                    failures.Add("login_with_xbox HTTP " + loginResult.Code + ": " + DescribeError(loginResult.Body) + loginResult.Diagnostics);
                }
                else
                {
                    var profile = await Http.GetAsync(McProfileUrl, "Bearer " + xboxToken, cancellationToken).ConfigureAwait(false);
                    AuthLog.Write("Profile (login_with_xbox token): HTTP " + profile.Code + " " + DescribeError(profile.Body) + profile.Diagnostics);
                    if (profile.IsOk)
                    {
                        mcToken = xboxToken;
                        expiresIn = xboxExpires;
                        profileBody = profile.Body;
                    }
                    else if (profile.Status == HttpStatusCode.NotFound)
                    {
                        throw new AuthException(NoProfileMessage);
                    }
                    else if (profile.IsRateLimited)
                    {
                        throw Throttled(profile, "Profile");
                    }
                    else
                    {
                        failures.Add("Profile HTTP " + profile.Code + ": " + DescribeError(profile.Body) + profile.Diagnostics);
                    }
                }
            }

            if (string.IsNullOrEmpty(profileBody) || string.IsNullOrEmpty(mcToken))
            {
                // One extra request, only after a refusal: if the entitlements endpoint accepts
                // the same token, the token is good and it is the profile call that Mojang
                // refuses — that separates a throttled connection from a rejected token, or an
                // app id that is not approved for the Minecraft API.
                failures.Add(await DescribeEntitlementAsync(lastToken, cancellationToken).ConfigureAwait(false));
                AuthBackoff.Register(300);
                throw new AuthException(DescribeFailure(failures));
            }

            // 4. Ownership: a profile exists only for an account with Minecraft: Java
            // Edition (or Xbox Game Pass), and /minecraft/profile answers 404 otherwise.
            var account = new Account
            {
                AccessToken = mcToken,
                RefreshToken = token.RefreshToken,
                Xuid = xuid,
                ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 86400)
            };
            ApplyProfile(account, profileBody);
            if (string.IsNullOrEmpty(account.Name) || string.IsNullOrEmpty(account.Uuid))
            {
                throw new AuthException("Minecraft returned an incomplete profile.");
            }

            // A working token proves the connection is not being throttled.
            AuthBackoff.Clear();
            return account;
        }

        /// <summary>
        /// Reuses a saved session instead of a whole new Microsoft sign-in: the stored Minecraft
        /// token is checked against /minecraft/profile, and only refreshed when it is refused.
        /// Returns null when the caller has to sign in, and never throws for a refused token, so
        /// a stale session cannot block signing in.
        /// </summary>
        public async Task<Account> ReuseAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null || string.IsNullOrEmpty(account.AccessToken)) return null;

            HttpResult profile;
            try
            {
                profile = await Http.GetAsync(McProfileUrl, "Bearer " + account.AccessToken, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Offline or a dead connection: report nothing usable and let the caller decide.
                return null;
            }
            AuthLog.Write("Saved session: HTTP " + profile.Code + " " + DescribeError(profile.Body) + profile.Diagnostics);
            if (profile.IsOk)
            {
                ApplyProfile(account, profile.Body);
                return account;
            }
            if (profile.IsRateLimited || AuthBackoff.IsActive)
            {
                return null;
            }
            if (string.IsNullOrEmpty(account.RefreshToken))
            {
                return null;
            }

            try
            {
                return await RefreshAsync(account, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthException)
            {
                return null;
            }
        }

        public async Task<Account> RefreshAsync(Account account, CancellationToken cancellationToken)
        {
            if (account == null || string.IsNullOrEmpty(account.RefreshToken))
            {
                throw new AuthException("No saved Microsoft session. Please sign in again.");
            }
            ThrowIfThrottled();

            var form = "grant_type=refresh_token"
                     + "&client_id=" + Uri.EscapeDataString(_clientId)
                     + "&refresh_token=" + Uri.EscapeDataString(account.RefreshToken)
                     + "&scope=" + Uri.EscapeDataString("XboxLive.SignIn XboxLive.offline_access");
            var result = await Http.PostFormAsync(TokenUrl, form, cancellationToken).ConfigureAwait(false);
            if (result.IsRateLimited) throw Throttled(result, "Token refresh");

            string access = null;
            string refresh = null;
            try
            {
                using (var document = JsonDocument.Parse(result.Body))
                {
                    if (result.IsOk)
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

        private static void ApplyProfile(Account account, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    account.Name = JsonHelp.GetString(document.RootElement, "name");
                    account.Uuid = JsonHelp.GetString(document.RootElement, "id");
                }
            }
            catch (JsonException)
            {
            }
        }

        /// <summary>
        /// Plain-language fixes for the Azure errors an app registration most often causes: a
        /// registration that is not open to personal accounts, one without public client flows,
        /// or one that was never given the Xbox Live permission.
        /// </summary>
        private static string HintsFor(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            if (body.Contains("AADSTS700016")) return " That app id is not known in the Microsoft Accounts tenant: register the app with the account type 'Personal Microsoft accounts only' and copy its Application (client) ID again.";
            if (body.Contains("AADSTS7000218")) return " The app registration must allow public client flows: Authentication → 'Allow public client flows' → Yes.";
            if (body.Contains("AADSTS65001")) return " Microsoft has not recorded consent for this app yet: sign in again and accept the screen it shows. Nothing has to be added under API permissions for Xbox Live — the XboxLive.signin scope travels with the sign-in request itself.";
            if (body.Contains("AADSTS70011") || body.Contains("invalid_scope")) return " The XboxLive.signin scope was refused: the app has to accept personal Microsoft accounts, and the scope is sent with the request (there is no Xbox Live permission to add).";
            if (body.Contains("AADSTS50059")) return " The app id belongs to a single-tenant registration: it has to be registered for personal Microsoft accounts.";
            return "";
        }

        private string DescribeFailure(List<string> failures)
        {
            var relevant = failures.FindAll(line => !string.IsNullOrEmpty(line));
            var text = new StringBuilder();
            text.Append("Could not sign in to Minecraft. ").Append(string.Join(" / ", relevant)).Append(".");
            text.Append(HintsFor(string.Join(" ", relevant)));
            text.Append(" Microsoft accepted the sign-in and Mojang issued a Minecraft token, but the profile was refused, so it is neither a wrong password nor a missing licence.");
            text.Append(" Read the entitlements line above: at 200 the token is accepted and only the profile call is refused — that is what an app id Mojang has not approved for the Minecraft API gets, and what a connection with too many attempts gets.");
            if (UsesCustomClientId)
            {
                text.Append(" You use your own app id: submit it for approval at https://aka.ms/mce-reviewappid — until Mojang approves it, api.minecraftservices.com keeps refusing it. Delete ")
                    .Append(ClientIdFilePath).Append(" to go back to the built-in id meanwhile.");
            }
            else
            {
                text.Append(" You use the built-in app id, which is approved, so the connection is the likely cause: wait a few hours before trying again, and turn a VPN off if you use one.");
            }
            text.Append(" Mojang's exact answers and their headers are in auth.log.");
            return text.ToString();
        }

        /// <summary>
        /// Asks the entitlements endpoint with the token that was just issued: it says whether
        /// Minecraft accepts the token at all, which is the difference between a refused token
        /// and a refused profile call. One request, and only after a sign-in has failed.
        /// </summary>
        private static async Task<string> DescribeEntitlementAsync(string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(token)) return "";
            HttpResult result;
            try
            {
                result = await Http.GetAsync(McEntitlementsUrl, "Bearer " + token, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return "";
            }
            AuthLog.Write("Entitlements: HTTP " + result.Code + " " + DescribeError(result.Body) + result.Diagnostics);
            if (!result.IsOk)
            {
                return "entitlements HTTP " + result.Code + ": " + DescribeError(result.Body) + result.Diagnostics;
            }
            var items = 0;
            try
            {
                using (var document = JsonDocument.Parse(result.Body))
                {
                    JsonElement array;
                    if (document.RootElement.TryGetProperty("items", out array) && array.ValueKind == JsonValueKind.Array)
                    {
                        items = array.GetArrayLength();
                    }
                }
            }
            catch (JsonException)
            {
            }
            return "entitlements HTTP 200 with " + items + " item(s)";
        }

        private static void ThrowIfThrottled()
        {
            if (!AuthBackoff.IsActive) return;
            throw new AuthException("The last attempt was refused or rate limited, so FastCraft is waiting before it sends Mojang another request. Try again in "
                + WaitText() + ".");
        }

        private static AuthException Throttled(HttpResult result, string step)
        {
            var seconds = result.RetryAfterSeconds > 0 ? result.RetryAfterSeconds : 600;
            AuthBackoff.Register(seconds);
            AuthLog.Write(step + ": HTTP 429, no further requests for " + (int)Math.Ceiling(seconds) + " s");
            return new AuthException("Mojang is throttling sign-in (HTTP 429 at " + step + "). Each extra attempt makes it worse, so FastCraft will not send more requests for "
                + WaitText() + " — then press Sign in with Microsoft once.");
        }

        private static string WaitText()
        {
            var minutes = AuthBackoff.RemainingMinutes;
            if (minutes <= 0) return "a few minutes";
            if (minutes == 1) return "about a minute";
            return "about " + minutes + " minutes";
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

        private static string ReadMinecraftToken(HttpResult result, out long expiresIn)
        {
            expiresIn = 0;
            if (!result.IsOk) return null;
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

        private static string DescribeError(string body)
        {
            if (string.IsNullOrEmpty(body)) return "(empty response)";
            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var root = document.RootElement;
                    var error = JsonHelp.GetString(root, "error");
                    if (string.IsNullOrEmpty(error)) error = JsonHelp.GetString(root, "errorType");
                    var description = JsonHelp.GetString(root, "error_description");
                    if (string.IsNullOrEmpty(description)) description = JsonHelp.GetString(root, "errorMessage");
                    var parts = "";
                    if (!string.IsNullOrEmpty(error)) parts = error;
                    if (!string.IsNullOrEmpty(description)) parts = string.IsNullOrEmpty(parts) ? description : parts + ": " + description;
                    if (parts.Length > 400) parts = parts.Substring(0, 400);
                    if (parts.Length > 0) return parts;
                    // A refusal whose JSON has no error field: show what Mojang actually sent,
                    // so the next report contains the real answer instead of a blank.
                    return Trimmed(body);
                }
            }
            catch (JsonException)
            {
                return Trimmed(body);
            }
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
