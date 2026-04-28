using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SkinUI : MonoBehaviour
{
    public Image skinImage;
    public TextMeshProUGUI skinName;
    public TextMeshProUGUI skinPrice;
    public TextMeshProUGUI statusText;
    public Button buyButton;
    // public TextMeshProUGUI buyButtonText;
    public List<Sprite> skinSprites; 
    public void SetSkinUI(int skinid, string skinname, long price, string status)
    {
        // Giả sử bạn có một hệ thống để lấy sprite từ asset key
        if (skinSprites != null && skinid >= 0 && skinid < skinSprites.Count)
        {
            skinImage.sprite = skinSprites[skinid];
        }
        skinName.text = "Name: "+skinname;
        skinPrice.text = "Price: " + price;
        statusText.text = "Status: "+ status;

        if (buyButton != null)
        {
            buyButton.gameObject.SetActive(true);
        }
    }
    public void SetSkinPlayerLobbyUI(int skinid, string playername, string status ="NONE")
    {
        // Giả sử bạn có một hệ thống để lấy sprite từ asset key
        if (skinSprites != null && skinid >= 0 && skinid < skinSprites.Count)
        {
            skinImage.sprite = skinSprites[skinid];
        }
        skinName.text = "Name: "+playername;
        skinPrice.gameObject.SetActive(false);
        statusText.text = ""+ status;

        // if (buyButton != null)
        // {
        //     buyButton.gameObject.SetActive(false);
        // }
    }
}
