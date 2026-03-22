using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class BombController : MonoBehaviour
{
    [Header("Bomb")]
    public KeyCode inputKey = KeyCode.LeftShift;
    public GameObject bombPrefab;
    public float bombFuseTime = 3f;
    public int bombAmount = 1;
    [SerializeField]private int bombsRemaining;
    public int explosionRadius = 1;


    [Header("Destructible")]
    public Tilemap destructibleTiles;
    public Destructible destructiblePrefab;

    private void OnEnable()
    {
        bombsRemaining = bombAmount;
    }

    private void Update()
    {
        if (bombsRemaining > 0 && Input.GetKeyDown(inputKey)) {
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
            bombit.Initialize(destructibleTiles, explosionRadius);
        }

        bombsRemaining--;

        yield return new WaitForSeconds(bombFuseTime);
        if(bomb == null)
        {
            //phongf trg hoop bi kick no trc
            bombsRemaining++;
            yield break;
        }
        position = bomb.transform.position;
        position.x = Mathf.Round(position.x);
        position.y = Mathf.Round(position.y);

        bomb.GetComponent<Bombit>().bombexplosion(position, destructibleTiles, explosionRadius );

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
        if (other.gameObject.layer == LayerMask.NameToLayer("Bomb")) {
            other.isTrigger = false;
        }
    }

}
