using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class LobbyJoinUI : MonoBehaviour
{
     [SerializeField]private Button closeButton;
    [SerializeField]private Button QuickJoinButton;
    [SerializeField]private Button CodeJoinButton;
    [SerializeField]private InputField lobbyCodeInputField;


    private void Awake() {
        QuickJoinButton.onClick.AddListener(() => {
            GameLobby.Instance.QuickJoin();
        });
        CodeJoinButton.onClick.AddListener(() =>
        {
            string lobbyCode = lobbyCodeInputField.text;
            if (!string.IsNullOrEmpty(lobbyCode))
            {
                GameLobby.Instance.JoinWithCode(lobbyCode);
            }
        });
        closeButton.onClick.AddListener(() => {
            Hide();
        });
    }
    public void Show() {
        gameObject.SetActive(true);
        GetComponent<CanvasGroup>().DOFade(1, 0.5f).SetEase(Ease.InOutSine);
    }   
    public void Hide() {
        GetComponent<CanvasGroup>().DOFade(0, 0.5f).SetEase(Ease.InOutSine).OnComplete(() => {
            gameObject.SetActive(false);
        });

    }
}
