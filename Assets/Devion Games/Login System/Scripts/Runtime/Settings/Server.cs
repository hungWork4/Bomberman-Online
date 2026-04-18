using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DevionGames.LoginSystem.Configuration
{
    [System.Serializable]
    public class Server : Settings
    {
        public override string Name
        {
            get
            {
                return "Server";
            }
        }


        [Header("Server Settings:")]
        public string serverAddress = "https://lndnirkrfjwriofnylks.supabase.co";
        public string supabaseAnonKey = "";
        public string usernameLookupRpc = "resolve_login_email";
        public string usernameLookupParameter = "p_username";
        public string resolvedEmailField = "email";
        public string profileTable = "player_profiles";
        public string profileUsernameField = "username";
        public string profileEmailField = "email";
        public string profileDisplayNameField = "display_name";
        public string accountKey = "Account";
        public string accessTokenKey = "SupabaseAccessToken";
        public string refreshTokenKey = "SupabaseRefreshToken";

    }
}