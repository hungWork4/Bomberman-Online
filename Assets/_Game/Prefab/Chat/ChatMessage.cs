using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatMessage : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private float verticalPadding = 18f;
    [SerializeField] private float minimumBubbleHeight = 36f;

    private LayoutElement layoutElement;

    void Awake()
    {
        layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = gameObject.AddComponent<LayoutElement>();
        }
    }

    public void SetText(string str)
    {
        if (messageText == null)
        {
            return;
        }

        messageText.text = str;
        messageText.ForceMeshUpdate();

        if (layoutElement != null)
        {
            float preferredHeight = messageText.preferredHeight + verticalPadding;
            layoutElement.preferredHeight = Mathf.Max(minimumBubbleHeight, preferredHeight);
        }
    }
}
