using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class BombController : MonoBehaviour
{
    [Header("Bomb")]
    [SerializeField] private KeyCode inputKey = KeyCode.LeftShift;
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private float bombFuseTime = 3f;
    [SerializeField] private int bombAmount = 1;
    [SerializeField] private int bombsRemaining;
    [SerializeField] private int explosionRadius = 1;
    public int ExplosionRadius { get => explosionRadius; set => explosionRadius = value; }

    [Header("Destructible")]
    public Tilemap destructibleTiles;
    public Destructible destructiblePrefab;

    private void OnEnable()
    {
        bombsRemaining = bombAmount;
        Debug.Log("load Destructibles");
        //if(destructibleTiles== null)
        //    destructibleTiles = GameObject.Find("Stage/Destructibles").GetComponent<Tilemap>();
    }

    private void Update()
    {
        if (bombsRemaining > 0 && Input.GetKeyDown(inputKey))
        {
            StartCoroutine(PlaceBomb());
        }
    }

    private IEnumerator PlaceBomb()
    {
        Vector2 position = transform.position;
        position.x = Mathf.Round(position.x);
        position.y = Mathf.Round(position.y);

        GameObject bomb = Instantiate(bombPrefab, position, Quaternion.identity);
        Bombit bombit = bomb.GetComponent<Bombit>();
        if (bombit != null)
        {
            bombit.Initialize(destructibleTiles, ExplosionRadius);
        }

        bombsRemaining--;

        yield return new WaitForSeconds(bombFuseTime);
        if (bomb == null)
        {
            //phongf trg hoop bi kick no trc
            bombsRemaining++;
            yield break;
        }
        position = bomb.transform.position;
        position.x = Mathf.Round(position.x);
        position.y = Mathf.Round(position.y);

        bomb.GetComponent<Bombit>().bombexplosion(position, destructibleTiles, ExplosionRadius);

        Destroy(bomb);
        bombsRemaining++;
    }
    public void AddBomb()
    {
        bombAmount++;
        bombsRemaining++;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Bomb"))
        {
            other.isTrigger = false;
        }
    }

}
