using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(MeshRenderer))]
public class FallingBlock : MonoBehaviour
{
    public VoxelWorld world;
    public BlockType type = BlockType.Sand;

    private Rigidbody rb;
    private float timeAlive;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Teljes forgás tiltás és X/Z tengely rögzítés
        rb.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.mass = 1f;
        rb.linearDamping = 0.1f; 
        
        // Kicsivel kisebb collider, hogy ne akadjon a falakba
        var col = gameObject.AddComponent<BoxCollider>();
        col.size = new Vector3(0.98f, 0.98f, 0.98f); 
    }

    void Update()
    {
        timeAlive += Time.deltaTime;
        if (transform.position.y < -64) Destroy(gameObject);
        
        // Ha nagyon lassan mozog és már élt egy kicsit, próbáljuk lerakni
        if (rb.linearVelocity.sqrMagnitude < 0.05f && timeAlive > 0.15f)
        {
            AttemptPlace();
        }
    }

    void AttemptPlace()
    {
        Vector3Int pos = new Vector3Int(
            Mathf.RoundToInt(transform.position.x - 0.5f),
            Mathf.RoundToInt(transform.position.y),
            Mathf.RoundToInt(transform.position.z - 0.5f)
        );

        BlockType current = world.GetBlock(pos);
        BlockType below = world.GetBlock(pos + Vector3Int.down);

        // Standard lerakás: levegőben/vízben vagyunk, alattunk szilárd
        if ((current == BlockType.Air || current == BlockType.Water) && below != BlockType.Air && below != BlockType.Water)
        {
            world.SetBlock(pos, type);
            Destroy(gameObject);
        }
        else
        {
            // Fallback: Ha beszorultunk egy blokkba, próbáljunk meg feljebb menni
            Vector3Int above = pos + Vector3Int.up;
            if (world.GetBlock(above) == BlockType.Air || world.GetBlock(above) == BlockType.Water)
            {
                 if (current != BlockType.Air && current != BlockType.Water)
                 {
                     world.SetBlock(above, type);
                     Destroy(gameObject);
                 }
            }
            // Ha túl sokáig ragad, töröljük
            else if (timeAlive > 5.0f) 
            {
                Destroy(gameObject);
            }
        }
    }
}