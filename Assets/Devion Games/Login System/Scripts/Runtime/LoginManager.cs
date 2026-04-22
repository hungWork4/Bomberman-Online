using DevionGames.LoginSystem.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Networking;

namespace DevionGames.LoginSystem
{
    public class LoginManager : MonoBehaviour
    {
		private static LoginManager m_Current;
			private const string PendingUsernameKey = "SupabasePendingUsername";
			private const string PendingEmailKey = "SupabasePendingEmail";
			private const string PendingDisplayNameKey = "SupabasePendingDisplayName";
			private const string SupabaseUserIdKey = "SupabaseUserId";

		/// <summary>
		/// The LoginManager singleton object. This object is set inside Awake()
		/// </summary>
		public static LoginManager current
		{
			get
			{
				Assert.IsNotNull(m_Current, "Requires Login Manager.Create one from Tools > Devion Games > Login System > Create Login Manager!");
				return m_Current;
			}
		}

		/// <summary>
		/// Awake is called when the script instance is being loaded.
		/// </summary>
		private void Awake()
		{
			if (LoginManager.m_Current != null)
			{
				if (LoginManager.DefaultSettings.debug)
					Debug.Log("Multiple LoginManager in scene...this is not supported. Destroying instance!");
				Destroy(gameObject);
				return;
			}
			else
			{
				LoginManager.m_Current = this;
				if(LoginManager.DefaultSettings.debug)
					Debug.Log("LoginManager initialized.");

			}
		}

        private void Start()
        {
			if (LoginManager.DefaultSettings.skipLogin)
			{
				if (LoginManager.DefaultSettings.debug)
					Debug.Log("Login System is disabled...Loading "+ LoginManager.DefaultSettings.sceneToLoad+" scene.");
				UnityEngine.SceneManagement.SceneManager.LoadScene(LoginManager.DefaultSettings.sceneToLoad);
			}
		}


        [SerializeField]
		private LoginConfigurations m_Configurations = null;

		/// <summary>
		/// Gets the login configurations. Configurate it inside the editor.
		/// </summary>
		/// <value>The database.</value>
		public static LoginConfigurations Configurations
		{
			get
			{
				if (LoginManager.current != null)
				{
					Assert.IsNotNull(LoginManager.current.m_Configurations, "Please assign Login Configurations to the Login Manager!");
					return LoginManager.current.m_Configurations;
				}
				return null;
			}
		}


		private static Default m_DefaultSettings;
		public static Default DefaultSettings
		{
			get
			{
				if (m_DefaultSettings == null)
				{
					m_DefaultSettings = GetSetting<Default>();
				}
				return m_DefaultSettings;
			}
		}

		private static UI m_UI;
		public static UI UI
		{
			get
			{
				if (m_UI == null)
				{
					m_UI = GetSetting<UI>();
				}
				return m_UI;
			}
		}

		private static Notifications m_Notifications;
		public static Notifications Notifications
		{
			get
			{
				if (m_Notifications == null)
				{
					m_Notifications = GetSetting<Notifications>();
				}
				return m_Notifications;
			}
		}

		private static Server m_Server;
		public static Server Server
		{
			get
			{
				if (m_Server == null)
				{
					m_Server = GetSetting<Server>();
				}
				return m_Server;
			}
		}

		private static T GetSetting<T>() where T : Configuration.Settings
		{
			if (LoginManager.Configurations != null)
			{
				return (T)LoginManager.Configurations.settings.Where(x => x.GetType() == typeof(T)).FirstOrDefault();
			}
			return default(T);
		}

		private static string EscapeJson(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}

		private static string ExtractJsonStringValue(string json, string key)
		{
			if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
			{
				return string.Empty;
			}

			System.Text.RegularExpressions.Match match =
				System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");

			if (match.Success && match.Groups.Count > 1)
			{
				return match.Groups[1].Value;
			}

			return string.Empty;
		}

		private static string GetSupabaseUrl(string route)
		{
			return LoginManager.Server.serverAddress.TrimEnd('/') + route;
		}

		private static bool IsEmailIdentifier(string identifier)
		{
			return !string.IsNullOrEmpty(identifier) && identifier.Contains("@");
		}

		private static string NormalizeJsonStringResponse(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				return string.Empty;
			}

			string trimmed = json.Trim();
			if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
			{
				return trimmed.Substring(1, trimmed.Length - 2);
			}

			return trimmed;
		}

		private static string ExtractFirstEmailFromJson(string json)
		{
			if (string.IsNullOrEmpty(json))
			{
				return string.Empty;
			}

			System.Text.RegularExpressions.Match match =
				System.Text.RegularExpressions.Regex.Match(json, "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}");

			if (match.Success)
			{
				return match.Value;
			}

			return string.Empty;
		}

		private static string FallbackUsernameFromEmail(string email)
		{
			if (string.IsNullOrEmpty(email) || !email.Contains("@"))
			{
				return string.Empty;
			}

			return email.Split('@')[0];
		}

		private static void CachePendingProfile(string username, string email, string displayName)
		{
			PlayerPrefs.SetString(PendingUsernameKey, username ?? string.Empty);
			PlayerPrefs.SetString(PendingEmailKey, email ?? string.Empty);
			PlayerPrefs.SetString(PendingDisplayNameKey, displayName ?? string.Empty);
			PlayerPrefs.Save();
		}

		private static void ClearPendingProfile()
		{
			PlayerPrefs.DeleteKey(PendingUsernameKey);
			PlayerPrefs.DeleteKey(PendingEmailKey);
			PlayerPrefs.DeleteKey(PendingDisplayNameKey);
			PlayerPrefs.Save();
		}

		private static UnityWebRequest CreateSupabaseJsonRequest(string url, string method, string payload, bool includeAuthToken)
		{
			UnityWebRequest request = new UnityWebRequest(url, method);
			byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);
			request.uploadHandler = new UploadHandlerRaw(bodyRaw);
			request.downloadHandler = new DownloadHandlerBuffer();

			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("apikey", LoginManager.Server.supabaseAnonKey);

			if (includeAuthToken)
			{
				string accessToken = PlayerPrefs.GetString(LoginManager.Server.accessTokenKey, string.Empty);
				if (!string.IsNullOrEmpty(accessToken))
				{
					request.SetRequestHeader("Authorization", "Bearer " + accessToken);
				}
			}

			return request;
		}

		private static UnityWebRequest CreateSupabaseGetRequest(string url)
		{
			UnityWebRequest request = UnityWebRequest.Get(url);
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("apikey", LoginManager.Server.supabaseAnonKey);
			request.SetRequestHeader("Authorization", "Bearer " + LoginManager.Server.supabaseAnonKey);
			return request;
		}

		private static UnityWebRequest CreateSupabaseProfileUpsertRequest(string payload)
		{
			string requestUrl = GetSupabaseUrl("/rest/v1/" + LoginManager.Server.profileTable + "?on_conflict=id");
			UnityWebRequest request = CreateSupabaseJsonRequest(requestUrl, UnityWebRequest.kHttpVerbPOST, payload, true);
			request.SetRequestHeader("Prefer", "resolution=merge-duplicates,return=minimal");
			return request;
		}

		private static IEnumerator ResolveEmailFromProfileQuery(string username, Action<string> onResolved, Action<string> onFailed)
		{
			string encodedUsername = UnityWebRequest.EscapeURL(username);
			string table = LoginManager.Server.profileTable;
			string select = LoginManager.Server.profileEmailField;
			string usernameField = LoginManager.Server.profileUsernameField;
			string requestUrl = GetSupabaseUrl("/rest/v1/" + table + "?select=" + select + "&" + usernameField + "=eq." + encodedUsername + "&limit=1");

			using (UnityWebRequest www = CreateSupabaseGetRequest(requestUrl))
			{
				yield return www.SendWebRequest();

				if (www.isNetworkError || www.isHttpError)
				{
					onFailed?.Invoke("profile-query: " + www.error + " " + www.downloadHandler.text);
					yield break;
				}

				string response = www.downloadHandler.text;
				string resolvedEmail = ExtractJsonStringValue(response, LoginManager.Server.profileEmailField);

				if (string.IsNullOrEmpty(resolvedEmail))
				{
					resolvedEmail = ExtractFirstEmailFromJson(response);
				}

				if (!string.IsNullOrEmpty(resolvedEmail))
				{
					onResolved?.Invoke(resolvedEmail);
				}
				else
				{
					onFailed?.Invoke("profile-query: empty response " + response);
				}
			}
		}

		private static IEnumerator ResolveEmailFromUsername(string username, Action<string> onResolved, Action onFailed)
		{
			string rpcUrl = GetSupabaseUrl("/rest/v1/rpc/" + LoginManager.Server.usernameLookupRpc);
			string payload = "{\"" + EscapeJson(LoginManager.Server.usernameLookupParameter) + "\":\"" + EscapeJson(username) + "\"}";

			using (UnityWebRequest www = CreateSupabaseJsonRequest(rpcUrl, UnityWebRequest.kHttpVerbPOST, payload, false))
			{
				www.SetRequestHeader("Authorization", "Bearer " + LoginManager.Server.supabaseAnonKey);
				yield return www.SendWebRequest();

				if (www.isNetworkError || www.isHttpError)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[LoginAccount] Username lookup failed: " + www.error + " " + www.downloadHandler.text);
				}

				string response = www.downloadHandler.text;
				string resolvedEmail = ExtractJsonStringValue(response, LoginManager.Server.resolvedEmailField);

				if (string.IsNullOrEmpty(resolvedEmail))
				{
					resolvedEmail = ExtractJsonStringValue(response, "email");
				}

				if (string.IsNullOrEmpty(resolvedEmail))
				{
					resolvedEmail = NormalizeJsonStringResponse(response);
				}

				if (string.IsNullOrEmpty(resolvedEmail) || !IsEmailIdentifier(resolvedEmail))
				{
					resolvedEmail = ExtractFirstEmailFromJson(response);
				}

				if (!string.IsNullOrEmpty(resolvedEmail) && IsEmailIdentifier(resolvedEmail))
				{
					onResolved?.Invoke(resolvedEmail);
					yield break;
				}

				bool fallbackResolved = false;
				yield return ResolveEmailFromProfileQuery(username,
					email =>
					{
						fallbackResolved = true;
						onResolved?.Invoke(email);
					},
					error =>
					{
						if (LoginManager.DefaultSettings.debug)
							Debug.Log("[LoginAccount] Username fallback lookup failed: " + error);
					});

				if (!fallbackResolved)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[LoginAccount] Username lookup response did not contain an email: " + response);
					onFailed?.Invoke();
				}
			}
		}

		private static IEnumerator UpsertPlayerProfileInternal(string userId, string username, string email, string displayName)
		{
			if (string.IsNullOrEmpty(userId))
			{
				yield break;
			}

			if (string.IsNullOrEmpty(username))
			{
				username = FallbackUsernameFromEmail(email);
			}

			string payload = "{" +
				"\"id\":\"" + EscapeJson(userId) + "\"," +
				"\"" + EscapeJson(LoginManager.Server.profileUsernameField) + "\":\"" + EscapeJson(username) + "\"," +
				"\"" + EscapeJson(LoginManager.Server.profileEmailField) + "\":\"" + EscapeJson(email) + "\"," +
				"\"" + EscapeJson(LoginManager.Server.profileDisplayNameField) + "\":\"" + EscapeJson(displayName) + "\"" +
				"}";

			using (UnityWebRequest www = CreateSupabaseProfileUpsertRequest(payload))
			{
				yield return www.SendWebRequest();

				if (www.isNetworkError || www.isHttpError)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[Profile] Upsert failed: " + www.error + " " + www.downloadHandler.text);
				}
				else if (LoginManager.DefaultSettings.debug)
				{
					Debug.Log("[Profile] Upsert success for user id: " + userId);
				}
			}
		}



		public static void CreateAccount(string username, string password, string email, string displayName)
		{
			if (LoginManager.current != null)
			{
				LoginManager.current.StartCoroutine(CreateAccountInternal(username, password, email, displayName));
			}
		}

		private static IEnumerator CreateAccountInternal(string username, string password, string email, string displayName)
		{
			if (LoginManager.Configurations == null)
			{
				EventHandler.Execute("OnFailedToCreateAccount");
				yield break;
			}
			if (LoginManager.DefaultSettings.debug)
				Debug.Log("[CreateAccount]: Trying to register a new account with email: " + email + "!");

			string payload = "{\"email\":\"" + EscapeJson(email) + "\",\"password\":\"" + EscapeJson(password) + "\",\"data\":{\"username\":\"" + EscapeJson(username) + "\",\"display_name\":\"" + EscapeJson(displayName) + "\"}}";

			using (UnityWebRequest www = CreateSupabaseJsonRequest(GetSupabaseUrl("/auth/v1/signup"), UnityWebRequest.kHttpVerbPOST, payload, false)) {
				yield return www.SendWebRequest();
				if (www.isNetworkError || www.isHttpError)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[CreateAccount] " + www.error + " " + www.downloadHandler.text);
					EventHandler.Execute("OnFailedToCreateAccount");
				}
				else {
					if (www.downloadHandler.text.Contains("\"user\"") || www.downloadHandler.text.Contains("\"id\""))
					{
						string response = www.downloadHandler.text;
						string accessToken = ExtractJsonStringValue(response, "access_token");
						string refreshToken = ExtractJsonStringValue(response, "refresh_token");
						string userId = ExtractJsonStringValue(response, "id");

						if (!string.IsNullOrEmpty(accessToken))
						{
							PlayerPrefs.SetString(LoginManager.Server.accessTokenKey, accessToken);
							PlayerPrefs.SetString(LoginManager.Server.refreshTokenKey, refreshToken);
							if (!string.IsNullOrEmpty(userId))
							{
								PlayerPrefs.SetString(SupabaseUserIdKey, userId);
							}
							PlayerPrefs.Save();
							SupabaseManager.SetAuthContext(userId, accessToken);
							yield return UpsertPlayerProfileInternal(userId, username, email, displayName);
							ClearPendingProfile();
						}
						else
						{
							// If signup requires email confirmation, wait until first login to upsert profile.
							CachePendingProfile(username, email, displayName);
						}

						if (LoginManager.DefaultSettings.debug)
							Debug.Log("[CreateAccount] Account creation was successfull!");
						EventHandler.Execute("OnAccountCreated");
					}else {
						if (LoginManager.DefaultSettings.debug)
							Debug.Log("[CreateAccount] Failed to create account.");
						EventHandler.Execute("OnFailedToCreateAccount");
					}
				}
			}
		}

		/// <summary>
		/// Logins the account.
		/// </summary>
		/// <param name="username">Username.</param>
		/// <param name="password">Password.</param>
		public static void LoginAccount(string username, string password)
		{
			if (LoginManager.current != null)
			{
				LoginManager.current.StartCoroutine(LoginAccountInternal(username, password));
			}
		}

		private static IEnumerator LoginAccountInternal(string username, string password)
		{
			if (LoginManager.Configurations == null)
			{
				EventHandler.Execute("OnFailedToLogin");
				yield break;
			}
			string loginIdentifier = username.Trim();
			string loginEmail = loginIdentifier;

			if (!IsEmailIdentifier(loginIdentifier))
			{
				bool resolved = false;
				yield return ResolveEmailFromUsername(loginIdentifier,
					email =>
					{
						loginEmail = email;
						resolved = true;
					},
					() => { resolved = false; });

				if (!resolved)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[LoginAccount] Could not resolve username to email.");
					EventHandler.Execute("OnFailedToLogin");
					yield break;
				}
			}

			if (LoginManager.DefaultSettings.debug)
				Debug.Log("[LoginAccount] Trying to login using identifier: " + loginIdentifier + "!");

			string payload = "{\"email\":\"" + EscapeJson(loginEmail) + "\",\"password\":\"" + EscapeJson(password) + "\"}";

			using (UnityWebRequest www = CreateSupabaseJsonRequest(GetSupabaseUrl("/auth/v1/token?grant_type=password"), UnityWebRequest.kHttpVerbPOST, payload, false))
			{
				yield return www.SendWebRequest();
				if (www.isNetworkError || www.isHttpError)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[LoginAccount] " + www.error + " " + www.downloadHandler.text);
					EventHandler.Execute("OnFailedToLogin");
				}
				else
				{
					string response = www.downloadHandler.text;
					if (response.Contains("\"access_token\""))
					{
						string accessToken = ExtractJsonStringValue(response, "access_token");
						string refreshToken = ExtractJsonStringValue(response, "refresh_token");
						string userId = ExtractJsonStringValue(response, "id");
						string email = ExtractJsonStringValue(response, "email");
						string metadataUsername = ExtractJsonStringValue(response, "username");
						string displayName = ExtractJsonStringValue(response, "display_name");

						string pendingUsername = PlayerPrefs.GetString(PendingUsernameKey, string.Empty);
						string pendingEmail = PlayerPrefs.GetString(PendingEmailKey, string.Empty);
						string pendingDisplayName = PlayerPrefs.GetString(PendingDisplayNameKey, string.Empty);

						PlayerPrefs.SetString(LoginManager.Server.accessTokenKey, accessToken);
						PlayerPrefs.SetString(LoginManager.Server.refreshTokenKey, refreshToken);
						PlayerPrefs.SetString(LoginManager.Server.accountKey, loginIdentifier);
						if (!string.IsNullOrEmpty(userId))
						{
							PlayerPrefs.SetString(SupabaseUserIdKey, userId);
						}
						PlayerPrefs.Save();
						SupabaseManager.SetAuthContext(userId, accessToken);
						_ = SupabaseManager.RefreshPlayerSnapshotAsync();

						string usernameToSave = metadataUsername;
						if (string.IsNullOrEmpty(usernameToSave) && pendingEmail == loginEmail)
						{
							usernameToSave = pendingUsername;
						}
						if (string.IsNullOrEmpty(usernameToSave) && !IsEmailIdentifier(loginIdentifier))
						{
							usernameToSave = loginIdentifier;
						}

						string displayNameToSave = displayName;
						if (string.IsNullOrEmpty(displayNameToSave) && pendingEmail == loginEmail)
						{
							displayNameToSave = pendingDisplayName;
						}

						yield return UpsertPlayerProfileInternal(userId, usernameToSave, email, displayNameToSave);
						if (!string.IsNullOrEmpty(userId))
						{
							ClearPendingProfile();
						}

						if (LoginManager.DefaultSettings.debug)
							Debug.Log("[LoginAccount] Login was successfull!");
						EventHandler.Execute("OnLogin");
					}
					else
					{
						if (LoginManager.DefaultSettings.debug)
							Debug.Log("[LoginAccount] Failed to login.");
						EventHandler.Execute("OnFailedToLogin");
					}
				}
			}
		}

		/// <summary>
		/// Recovers the password.
		/// </summary>
		/// <param name="email">Email.</param>
		public static void RecoverPassword(string email)
		{
			if (LoginManager.current != null)
			{
				LoginManager.current.StartCoroutine(RecoverPasswordInternal(email));
			}
		}

		private static IEnumerator RecoverPasswordInternal(string email)
		{
			if (LoginManager.Configurations == null)
			{
				EventHandler.Execute("OnFailedToRecoverPassword");
				yield break;
			}
			if (LoginManager.DefaultSettings.debug)
				Debug.Log("[RecoverPassword] Trying to recover password using email: " + email + "!");

			string payload = "{\"email\":\"" + EscapeJson(email) + "\"}";

			using (UnityWebRequest www = CreateSupabaseJsonRequest(GetSupabaseUrl("/auth/v1/recover"), UnityWebRequest.kHttpVerbPOST, payload, false))
			{
				yield return www.SendWebRequest();
				if (www.isNetworkError || www.isHttpError)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[RecoverPassword] " + www.error + " " + www.downloadHandler.text);
					EventHandler.Execute("OnFailedToRecoverPassword");
				}
				else
				{
					EventHandler.Execute("OnPasswordRecovered");
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[RecoverPassword] Recovery email sent.");
				}
			}
		}

		/// <summary>
		/// Resets the password.
		/// </summary>
		/// <param name="username">Username.</param>
		/// <param name="password">Password.</param>
		public static void ResetPassword(string username, string password)
		{
			if (LoginManager.current != null)
			{
				LoginManager.current.StartCoroutine(ResetPasswordInternal(username, password));
			}
		}

		private static IEnumerator ResetPasswordInternal(string username, string password)
		{
			if (LoginManager.Configurations == null)
			{
				EventHandler.Execute("OnFailedToResetPassword");
				yield break;
			}
			if (LoginManager.DefaultSettings.debug)
				Debug.Log("[ResetPassword] Trying to reset password for current authenticated user.");

			string accessToken = PlayerPrefs.GetString(LoginManager.Server.accessTokenKey, string.Empty);
			if (string.IsNullOrEmpty(accessToken))
			{
				if (LoginManager.DefaultSettings.debug)
					Debug.Log("[ResetPassword] Missing access token. Please login first.");
				EventHandler.Execute("OnFailedToResetPassword");
				yield break;
			}

			string payload = "{\"password\":\"" + EscapeJson(password) + "\"}";

			using (UnityWebRequest www = CreateSupabaseJsonRequest(GetSupabaseUrl("/auth/v1/user"), UnityWebRequest.kHttpVerbPUT, payload, true))
			{
				yield return www.SendWebRequest();
				if (www.isNetworkError || www.isHttpError)
				{
					if (LoginManager.DefaultSettings.debug)
						Debug.Log("[ResetPassword] " + www.error + " " + www.downloadHandler.text);
					EventHandler.Execute("OnFailedToResetPassword");
				}
				else
				{
					if (www.downloadHandler.text.Contains("\"id\""))
					{
						if (LoginManager.DefaultSettings.debug)
							Debug.Log("[RecoverPassword] Password resetted!");
						EventHandler.Execute("OnPasswordResetted");
				
					}
					else
					{
						if (LoginManager.DefaultSettings.debug)
							Debug.Log("Failed to reset password.");
						EventHandler.Execute("OnFailedToResetPassword");
					}
				}
			}
		}

		/// <summary>
		/// Validates the email.
		/// </summary>
		/// <returns><c>true</c>, if email was validated, <c>false</c> otherwise.</returns>
		/// <param name="email">Email.</param>
		public static bool ValidateEmail(string email)
		{
			System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$");
			System.Text.RegularExpressions.Match match = regex.Match(email);
			if (match.Success)
			{
				if (LoginManager.DefaultSettings.debug)
					Debug.Log("Email validation was successfull for email: " + email + "!");
			}
			else
			{
				if (LoginManager.DefaultSettings.debug)
					Debug.Log("Email validation failed for email: " + email + "!");
			}

			return match.Success;
		}
	}
}