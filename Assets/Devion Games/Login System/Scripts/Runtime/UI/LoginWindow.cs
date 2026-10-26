using DevionGames.UIWidgets;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DevionGames.LoginSystem
{
    public class LoginWindow : UIWidget
    {
        public override string[] Callbacks
        {
            get
            {
                List<string> callbacks = new List<string>(base.Callbacks);
                callbacks.Add("OnLogin");
                callbacks.Add("OnFailedToLogin");
                return callbacks.ToArray();
            }
        }

        [Header("Reference")]
        /// <summary>
		/// Referenced UI field
		/// </summary>
		[SerializeField]
        protected InputField username;
        /// <summary>
        /// Referenced UI field
        /// </summary>
        [SerializeField]
        protected InputField password;
        /// <summary>
        /// Referenced UI field
        /// </summary>
        [SerializeField]
        protected Toggle rememberMe;
        /// <summary>
        /// Referenced UI field
        /// </summary>
        [SerializeField]
        protected Button loginButton;
        [SerializeField]
        protected GameObject loadingIndicator;

        protected override void OnStart()
        {
            base.OnStart();
            username.text = PlayerPrefs.GetString("username", string.Empty);
            password.text = PlayerPrefs.GetString("password", string.Empty);

            if (rememberMe != null)
            {
                rememberMe.isOn = string.IsNullOrEmpty(username.text) ? false : true;
            }
          
            if (loadingIndicator != null){
                loadingIndicator.SetActive(false);
            }

            EventHandler.Register("OnLogin", OnLogin);
            EventHandler.Register("OnFailedToLogin", OnFailedToLogin);

            loginButton.onClick.AddListener(LoginUsingFields);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.F12) || Input.GetKeyDown(KeyCode.BackQuote))
            {
                TriggerDevBypass();
            }
        }

        private void OnGUI()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
            buttonStyle.normal.textColor = Color.yellow;

            // Nút bấm ở góc trái dưới màn hình
            if (GUI.Button(new Rect(20, Screen.height - 60, 210, 40), "⚡ Quick Dev Test (F1)", buttonStyle))
            {
                TriggerDevBypass();
            }
        }

        public void TriggerDevBypass()
        {
            string devName = "Tester_" + Random.Range(100, 999);
            string devId = "dev_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

            if (username != null && !string.IsNullOrWhiteSpace(username.text))
            {
                string custom = username.text.Trim();
                if (!custom.Equals("dev", System.StringComparison.OrdinalIgnoreCase) &&
                    !custom.Equals("test", System.StringComparison.OrdinalIgnoreCase) &&
                    !custom.Equals("cheat", System.StringComparison.OrdinalIgnoreCase))
                {
                    devName = custom;
                }
            }

            SupabaseManager.SetMockPlayerSnapshot(devName, devId);

            Debug.Log($"<color=cyan>[DevCheat] Bypassed login as: {devName} (ID: {devId})</color>");

            string targetScene = LoginManager.DefaultSettings != null && !string.IsNullOrEmpty(LoginManager.DefaultSettings.sceneToLoad)
                ? LoginManager.DefaultSettings.sceneToLoad
                : "MainMenu";

            UnityEngine.SceneManagement.SceneManager.LoadScene(targetScene);
        }
#endif

        public void LoginUsingFields() {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (username != null && !string.IsNullOrEmpty(username.text))
            {
                string input = username.text.Trim().ToLower();
                if (input == "dev" || input == "test" || input == "cheat")
                {
                    TriggerDevBypass();
                    return;
                }
            }
#endif
            LoginManager.LoginAccount(username.text, password.text);
            loginButton.interactable = false;
            if (loadingIndicator != null) {
                loadingIndicator.SetActive(true);
            }
        }

        private void OnLogin() {
            if (rememberMe != null && rememberMe.isOn)
            {
                PlayerPrefs.SetString("username", username.text);
                PlayerPrefs.SetString("password", password.text);
            }
            else
            {
                PlayerPrefs.DeleteKey("username");
                PlayerPrefs.DeleteKey("password");
            }
            Execute("OnLogin",new CallbackEventData());
            if (LoginManager.DefaultSettings.loadSceneOnLogin)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(LoginManager.DefaultSettings.sceneToLoad);
            }
        }

        private void OnFailedToLogin() {
            Execute("OnFailedToLogin", new CallbackEventData());
            username.text = "";
            password.text = "";
            LoginManager.Notifications.loginFailed.Show( delegate (int result) { Show(); }, "OK");
            loginButton.interactable = true;
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
            }
            Close();
        }
    }
}