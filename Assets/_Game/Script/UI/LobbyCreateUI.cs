using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyCreateUI : MonoBehaviour
{
    [SerializeField]private Button closeButton;
    [SerializeField]private Button createPublicButton;
    [SerializeField]private Button createPrivateButton;
    [SerializeField]private InputField lobbyNameInputField;


    private void Awake() {
        createPublicButton.onClick.AddListener(() => {
            GameLobby.Instance.CreateLobby(lobbyNameInputField.text, false);
        });
        createPrivateButton.onClick.AddListener(() => {
            GameLobby.Instance.CreateLobby(lobbyNameInputField.text, true);
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
