using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SavePoint : MonoBehaviour
{
    [SerializeField] private Sprite inactiveSprite;
    [SerializeField] private Sprite activeSprite;

    private SpriteRenderer spriteRenderer;
    private static SavePoint activeSavePoint;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = inactiveSprite;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        PlayerController player = collision.GetComponent<PlayerController>();
        if (player.IsOwner)
        {
            if (activeSavePoint != null)
            {
                activeSavePoint.spriteRenderer.sprite = activeSavePoint.inactiveSprite;
            }

            activeSavePoint = this;
            spriteRenderer.sprite = activeSprite;

            Vector2 calculatedSpawnPoint = new(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f
            );

            player.SetSpawnPoint(calculatedSpawnPoint);
        }
    }
}