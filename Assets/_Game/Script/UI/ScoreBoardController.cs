using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SocialPlatforms.Impl;

public class ScoreBoardController : MonoBehaviour
{
    public static ScoreBoardController Instance { get; private set; }

    [SerializeField] private PlayerCard playerCardPrefab;
    [SerializeField] private Transform playerCardContainer;
    public Dictionary<ulong, PlayerCard> playerCardsDict = new Dictionary<ulong, PlayerCard>();
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public void SpawnPlayerCard(PlayerController player)
    {
        if (playerCardsDict.ContainsKey(player.OwnerClientId))        {
            Debug.LogWarning($"Player card for client {player.OwnerClientId} already exists.");
            return;
        }
        PlayerCard playerCard = Instantiate(playerCardPrefab, playerCardContainer);
        playerCardsDict.Add(player.OwnerClientId, playerCard);

    }
    public void UpdatePlayerCard(PlayerController player)
    {
        PlayerCard playerCard = playerCardsDict[player.OwnerClientId];
        var bombController = player.GetComponent<BombController>(); 
        if(playerCard!= null && bombController != null)
        {
            playerCard.SetPlayerCard((int)player._currentSkinID.Value, player._CurrentPlayerName.Value.ToString()
            , player.Life, bombController.BombAmount, bombController.ExplosionRadius, player.moveSpeed);
        }else
        {
            Debug.LogWarning($"Player card or bomb controller for client {player.OwnerClientId} is missing.");
        }
    }
}
