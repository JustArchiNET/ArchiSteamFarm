using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SteamAuth
{
    /// <summary>
    /// Handles the linking process for a new mobile authenticator.
    /// </summary>
    public class AuthenticatorLinker
    {
       

        /// <summary>
        /// Set to register a new phone number when linking. If a phone number is not set on the account, this must be set. If a phone number is set on the account, this must be null.
        /// </summary>
        public string PhoneNumber = null;

        /// <summary>
        /// Randomly-generated device ID. Should only be generated once per linker.
        /// </summary>
        public string DeviceID { get; private set; }

        /// <summary>
        /// After the initial link step, if successful, this will be the SteamGuard data for the account. PLEASE save this somewhere after generating it; it's vital data.
        /// </summary>
        public SteamGuardAccount LinkedAccount { get; private set; }

        /// <summary>
        /// True if the authenticator has been fully finalized.
        /// </summary>
        public bool Finalized = false;

        private SessionData _session;
        private CookieContainer _cookies;

        public AuthenticatorLinker(SessionData session)
        {
            this._session = session;
            this.DeviceID = _generateDeviceID();

            this._cookies = new CookieContainer();
            session.AddCookies(_cookies);
        }

        public LinkResult AddAuthenticator()
        {
            bool hasPhone = _hasPhoneAttached();
            if (hasPhone && PhoneNumber != null)
                return LinkResult.MustRemovePhoneNumber;
            if (!hasPhone && PhoneNumber == null)
                return LinkResult.MustProvidePhoneNumber;

            if (!hasPhone)
            {
                if (!_addPhoneNumber())
                {
                    return LinkResult.GeneralFailure;
                }
            }

            var postData = new NameValueCollection();
            postData.Add("access_token", _session.OAuthToken);
            postData.Add("steamid", _session.SteamID.ToString());
            postData.Add("authenticator_type", "1");
            postData.Add("device_identifier", this.DeviceID);
            postData.Add("sms_phone_id", "1");

            string response = SteamWeb.MobileLoginRequest(APIEndpoints.STEAMAPI_BASE + "/ITwoFactorService/AddAuthenticator/v0001", "POST", postData);
            if (response == null) return LinkResult.GeneralFailure;

            var addAuthenticatorResponse = JsonConvert.DeserializeObject<AddAuthenticatorResponse>(response);
            if (addAuthenticatorResponse == null || addAuthenticatorResponse.Response == null || addAuthenticatorResponse.Response.Status != 1)
            {
                return LinkResult.GeneralFailure;
            }

            this.LinkedAccount = addAuthenticatorResponse.Response;
            LinkedAccount.Session = this._session;
            LinkedAccount.DeviceID = this.DeviceID;

            return LinkResult.AwaitingFinalization;
        }

        public FinalizeResult FinalizeAddAuthenticator(string smsCode)
        {
            bool smsCodeGood = false;

            var postData = new NameValueCollection();
            postData.Add("steamid", _session.SteamID.ToString());
            postData.Add("access_token", _session.OAuthToken);
            postData.Add("activation_code", smsCode);
            postData.Add("authenticator_code", "");
            int tries = 0;
            while (tries <= 30)
            {
                postData.Set("authenticator_code", tries == 0 ? "" : LinkedAccount.GenerateSteamGuardCode());
                postData.Add("authenticator_time", TimeAligner.GetSteamTime().ToString());

                if(smsCodeGood)
                    postData.Set("activation_code", "");

                string response = SteamWeb.MobileLoginRequest(APIEndpoints.STEAMAPI_BASE + "/ITwoFactorService/FinalizeAddAuthenticator/v0001", "POST", postData);
                if (response == null) return FinalizeResult.GeneralFailure;

                var finalizeResponse = JsonConvert.DeserializeObject<FinalizeAuthenticatorResponse>(response);

                if (finalizeResponse == null || finalizeResponse.Response == null)
                {
                    return FinalizeResult.GeneralFailure;
                }

                if(finalizeResponse.Response.Status == 89)
                {
                    return FinalizeResult.BadSMSCode;
                }

                if(finalizeResponse.Response.Status == 88)
                {
                    if(tries >= 30)
                    {
                        return FinalizeResult.UnableToGenerateCorrectCodes;
                    }
                }

                if (!finalizeResponse.Response.Success)
                {
                    return FinalizeResult.GeneralFailure;
                }

                if (finalizeResponse.Response.WantMore) 
                {
                    smsCodeGood = true;
                    tries++;
                    continue;
                }

                this.LinkedAccount.FullyEnrolled = true;
                return FinalizeResult.Success;
            }

            return FinalizeResult.GeneralFailure;
        }

        private bool _addPhoneNumber()
        {
            string response = SteamWeb.Request(APIEndpoints.COMMUNITY_BASE + "/steamguard/phoneajax?op=add_phone_number&arg=" + WebUtility.UrlEncode(PhoneNumber), "GET", null, _cookies);
            if (response == null) return false;

            var addPhoneNumberResponse = JsonConvert.DeserializeObject<AddPhoneResponse>(response);
            return addPhoneNumberResponse.Success;
        }

        private bool _hasPhoneAttached()
        {
            var postData = new NameValueCollection();
            postData.Add("op", "has_phone");
            postData.Add("arg", "null");
            string response = SteamWeb.MobileLoginRequest(APIEndpoints.COMMUNITY_BASE + "/steamguard/phoneajax", "GET", postData, _cookies);
            if (response == null) return false;

            var hasPhoneResponse = JsonConvert.DeserializeObject<HasPhoneResponse>(response);
            return hasPhoneResponse.HasPhone;
        }

        public enum LinkResult
        {
            MustProvidePhoneNumber, //No phone number on the account
            MustRemovePhoneNumber, //A phone number is already on the account
            AwaitingFinalization, //Must provide an SMS code
            GeneralFailure //General failure (really now!)
        }

        public enum FinalizeResult
        {
            BadSMSCode,
            UnableToGenerateCorrectCodes,
            Success,
            GeneralFailure
        }

        private class AddAuthenticatorResponse
        {
            [JsonProperty("response")]
            public SteamGuardAccount Response { get; set; }
        }

        private class FinalizeAuthenticatorResponse
        {
            [JsonProperty("response")]
            public FinalizeAuthenticatorInternalResponse Response { get; set; }

            internal class FinalizeAuthenticatorInternalResponse
            {
                [JsonProperty("status")]
                public int Status { get; set; }

                [JsonProperty("server_time")]
                public long ServerTime { get; set; }

                [JsonProperty("want_more")]
                public bool WantMore { get; set; }

                [JsonProperty("success")]
                public bool Success { get; set; }
            }
        }

        private class HasPhoneResponse
        {
            [JsonProperty("has_phone")]
            public bool HasPhone { get; set; }
        }

        private class AddPhoneResponse
        {
            [JsonProperty("success")]
            public bool Success { get; set; }
        }

        private string _generateDeviceID()
        {
            using (var sha1 = new SHA1Managed())
            {
                RNGCryptoServiceProvider secureRandom = new RNGCryptoServiceProvider();
                byte[] randomBytes = new byte[8];
                secureRandom.GetBytes(randomBytes);

                byte[] hashedBytes = sha1.ComputeHash(randomBytes);
                return "android:" + BitConverter.ToString(hashedBytes).Replace("-", "");
            }
        }
    }
}
