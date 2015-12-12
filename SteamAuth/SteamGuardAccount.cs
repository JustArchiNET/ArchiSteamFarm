using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamAuth
{
    public class SteamGuardAccount
    {
        [JsonProperty("shared_secret")]
        public string SharedSecret { get; set; }

        [JsonProperty("serial_number")]
        public string SerialNumber { get; set; }

        [JsonProperty("revocation_code")]
        public string RevocationCode { get; set; }

        [JsonProperty("uri")]
        public string URI { get; set; }

        [JsonProperty("server_time")]
        public long ServerTime { get; set; }

        [JsonProperty("account_name")]
        public string AccountName { get; set; }

        [JsonProperty("token_gid")]
        public string TokenGID { get; set; }

        [JsonProperty("identity_secret")]
        public string IdentitySecret { get; set; }

        [JsonProperty("secret_1")]
        public string Secret1 { get; set; }

        [JsonProperty("status")]
        public int Status { get; set; }

        [JsonProperty("device_id")]
        public string DeviceID { get; set; }

        /// <summary>
        /// Set to true if the authenticator has actually been applied to the account.
        /// </summary>
        [JsonProperty("fully_enrolled")]
        public bool FullyEnrolled { get; set; }

        public SessionData Session { get; set; }

        private static byte[] steamGuardCodeTranslations = new byte[] { 50, 51, 52, 53, 54, 55, 56, 57, 66, 67, 68, 70, 71, 72, 74, 75, 77, 78, 80, 81, 82, 84, 86, 87, 88, 89 };

        public bool DeactivateAuthenticator()
        {
            var postData = new NameValueCollection();
            postData.Add("steamid", this.Session.SteamID.ToString());
            postData.Add("steamguard_scheme", "2");
            postData.Add("revocation_code", this.RevocationCode);
            postData.Add("access_token", this.Session.OAuthToken);

            try
            {
                string response = SteamWeb.MobileLoginRequest(APIEndpoints.STEAMAPI_BASE + "/ITwoFactorService/RemoveAuthenticator/v0001", "POST", postData);
                var removeResponse = JsonConvert.DeserializeObject<RemoveAuthenticatorResponse>(response);

                if (removeResponse == null || removeResponse.Response == null || !removeResponse.Response.Success) return false;
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public string GenerateSteamGuardCode()
        {
            return GenerateSteamGuardCodeForTime(TimeAligner.GetSteamTime());
        }

        public string GenerateSteamGuardCodeForTime(long time)
        {
            if (this.SharedSecret == null || this.SharedSecret.Length == 0)
            {
                return "";
            }

            byte[] sharedSecretArray = Convert.FromBase64String(this.SharedSecret);
            byte[] timeArray = new byte[8];

            time /= 30L;

            for (int i = 8; i > 0; i--)
            {
                timeArray[i - 1] = (byte)time;
                time >>= 8;
            }

            HMACSHA1 hmacGenerator = new HMACSHA1();
            hmacGenerator.Key = sharedSecretArray;
            byte[] hashedData = hmacGenerator.ComputeHash(timeArray);
            byte[] codeArray = new byte[5];
            try
            {
                byte b = (byte)(hashedData[19] & 0xF);
                int codePoint = (hashedData[b] & 0x7F) << 24 | (hashedData[b + 1] & 0xFF) << 16 | (hashedData[b + 2] & 0xFF) << 8 | (hashedData[b + 3] & 0xFF);

                for (int i = 0; i < 5; ++i)
                {
                    codeArray[i] = steamGuardCodeTranslations[codePoint % steamGuardCodeTranslations.Length];
                    codePoint /= steamGuardCodeTranslations.Length;
                }
            }
            catch (Exception e)
            {
                return null; //Change later, catch-alls are bad!
            }
            return Encoding.UTF8.GetString(codeArray);
        }

        public Confirmation[] FetchConfirmations()
        {
            string url = this.GenerateConfirmationURL();

            CookieContainer cookies = new CookieContainer();
            this.Session.AddCookies(cookies);

            string response = SteamWeb.Request(url, "GET", null, cookies);

            /*So you're going to see this abomination and you're going to be upset.
              It's understandable. But the thing is, regex for HTML -- while awful -- makes this way faster than parsing a DOM, plus we don't need another library.
              And because the data is always in the same place and same format... It's not as if we're trying to naturally understand HTML here. Just extract strings.
              I'm sorry. */

            Regex confIDRegex = new Regex("data-confid=\"(\\d+)\"");
            Regex confKeyRegex = new Regex("data-key=\"(\\d+)\"");
            Regex confDescRegex = new Regex("<div>((Confirm|Trade with|Sell -) .+)</div>");

            if (response == null || !(confIDRegex.IsMatch(response) && confKeyRegex.IsMatch(response) && confDescRegex.IsMatch(response)))
            {
                if (response == null || !response.Contains("<div>Nothing to confirm</div>"))
                {
                    throw new WGTokenInvalidException();
                }

                return new Confirmation[0];
            }

            MatchCollection confIDs = confIDRegex.Matches(response);
            MatchCollection confKeys = confKeyRegex.Matches(response);
            MatchCollection confDescs = confDescRegex.Matches(response);

            List<Confirmation> ret = new List<Confirmation>();
            for (int i = 0; i < confIDs.Count; i++)
            {
                string confID = confIDs[i].Groups[1].Value;
                string confKey = confKeys[i].Groups[1].Value;
                string confDesc = confDescs[i].Groups[1].Value;
                Confirmation conf = new Confirmation()
                {
                    ConfirmationDescription = confDesc,
                    ConfirmationID = confID,
                    ConfirmationKey = confKey
                };
                ret.Add(conf);
            }

            return ret.ToArray();
        }

        public bool AcceptConfirmation(Confirmation conf)
        {
            return _sendConfirmationAjax(conf, "allow");
        }

        public bool DenyConfirmation(Confirmation conf)
        {
            return _sendConfirmationAjax(conf, "cancel");
        }

        /// <summary>
        /// Refreshes the Steam session. Necessary to perform confirmations if your session has expired or changed.
        /// </summary>
        /// <returns></returns>
        public bool RefreshSession()
        {
            string url = APIEndpoints.MOBILEAUTH_GETWGTOKEN;
            NameValueCollection postData = new NameValueCollection();
            postData.Add("access_token", this.Session.OAuthToken);

            string response = SteamWeb.Request(url, "POST", postData);
            if (response == null) return false;

            try
            {
                var refreshResponse = JsonConvert.DeserializeObject<RefreshSessionDataResponse>(response);
                if (refreshResponse == null || refreshResponse.Response == null || String.IsNullOrEmpty(refreshResponse.Response.Token))
                    return false;

                string token = this.Session.SteamID + "%7C%7C" + refreshResponse.Response.Token;
                string tokenSecure = this.Session.SteamID + "%7C%7C" + refreshResponse.Response.TokenSecure;

                this.Session.SteamLogin = token;
                this.Session.SteamLoginSecure = tokenSecure;
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        private bool _sendConfirmationAjax(Confirmation conf, string op)
        {
            string url = APIEndpoints.COMMUNITY_BASE + "/mobileconf/ajaxop";
            string queryString = "?op=" + op + "&";
            queryString += GenerateConfirmationQueryParams(op);
            queryString += "&cid=" + conf.ConfirmationID + "&ck=" + conf.ConfirmationKey;
            url += queryString;

            CookieContainer cookies = new CookieContainer();
            this.Session.AddCookies(cookies);
            string referer = GenerateConfirmationURL();

            string response = SteamWeb.Request(url, "GET", null, cookies, null);
            if (response == null) return false;

            SendConfirmationResponse confResponse = JsonConvert.DeserializeObject<SendConfirmationResponse>(response);
            return confResponse.Success;
        }

        public string GenerateConfirmationURL(string tag = "conf")
        {
            string endpoint = APIEndpoints.COMMUNITY_BASE + "/mobileconf/conf?";
            string queryString = GenerateConfirmationQueryParams(tag);
            return endpoint + queryString;
        }

        public string GenerateConfirmationQueryParams(string tag)
        {
            long time = TimeAligner.GetSteamTime();
            return "p=" + this.DeviceID + "&a=" + this.Session.SteamID.ToString() + "&k=" + _generateConfirmationHashForTime(time, tag) + "&t=" + time + "&m=android&tag=" + tag;
        }

        private string _generateConfirmationHashForTime(long time, string tag)
        {
            byte[] decode = Convert.FromBase64String(this.IdentitySecret);
            int n2 = 8;
            if (tag != null)
            {
                if (tag.Length > 32)
                {
                    n2 = 8 + 32;
                }
                else
                {
                    n2 = 8 + tag.Length;
                }
            }
            byte[] array = new byte[n2];
            int n3 = 8;
            while (true)
            {
                int n4 = n3 - 1;
                if (n3 <= 0)
                {
                    break;
                }
                array[n4] = (byte)time;
                time >>= 8;
                n3 = n4;
            }
            if (tag != null)
            {
                Array.Copy(Encoding.UTF8.GetBytes(tag), 0, array, 8, n2 - 8);
            }

            try
            {
                HMACSHA1 hmacGenerator = new HMACSHA1();
                hmacGenerator.Key = decode;
                byte[] hashedData = hmacGenerator.ComputeHash(array);
                string encodedData = Convert.ToBase64String(hashedData, Base64FormattingOptions.None);
                string hash = WebUtility.UrlEncode(encodedData);
                return hash;
            }
            catch (Exception e)
            {
                return null; //Fix soon: catch-all is BAD!
            }
        }

        //TODO: Determine how to detect an invalid session.
        public class WGTokenInvalidException : Exception
        {
        }

        private class RefreshSessionDataResponse
        {
            [JsonProperty("response")]
            public RefreshSessionDataInternalResponse Response { get; set; }
            internal class RefreshSessionDataInternalResponse
            {
                [JsonProperty("token")]
                public string Token { get; set; }

                [JsonProperty("token_secure")]
                public string TokenSecure { get; set; }
            }
        }

        private class RemoveAuthenticatorResponse
        {
            [JsonProperty("response")]
            public RemoveAuthenticatorInternalResponse Response { get; set; }

            internal class RemoveAuthenticatorInternalResponse
            {
                [JsonProperty("success")]
                public bool Success { get; set; }
            }
        }

        private class SendConfirmationResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
        }
    }
}
