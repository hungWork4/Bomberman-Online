using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;
using UnityEngine.UI;

public class PlayerCard : NetworkBehaviour
{
    [SerializeField]private Image Skin;
    [SerializeField]private Text Nametxt;
    [SerializeField]private Text Lifetxt;
    [SerializeField]private Text Bombtxt;
    [SerializeField]private Text ExploreRangetxt;
    [SerializeField]private Text Speedtxt;
    public List<Sprite> Skins;


    // [ServerRpc(RequireOwnership = false)]
    // public void SetPlayerCard(int skinId, string name, int life, int bomb, int exploreRange, float speed)
    // {
    //     SetPlayerCard(skinId, name, life, bomb, exploreRange, speed);
    // }
    // [ClientRpc]
    public void SetPlayerCard(int skinId, string name, int life, int bomb, int exploreRange, float speed)
    {
        Debug.Log("Skin id: "+ skinId);
        Debug.Log("Skin id: "+  Skins[skinId]== null);

        Skin.sprite = Skins[skinId];
        Nametxt.text = name;
        Lifetxt.text = ": " + life.ToString();
        Bombtxt.text = ": " + bomb.ToString();
        ExploreRangetxt.text = ": " + exploreRange.ToString();
        Speedtxt.text = ": " + (int)speed;
    }
}
