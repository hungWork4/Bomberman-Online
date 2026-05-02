using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class EndCard : MonoBehaviour
{
    public TextMeshProUGUI resultText;
    public CanvasGroup canvasGroup;

    private void Start()
    {
        Hide();
    }

    public void ShowResult(string text)
    {
        if (resultText != null)
        {
            resultText.text = text;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
        else
        {
            gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
