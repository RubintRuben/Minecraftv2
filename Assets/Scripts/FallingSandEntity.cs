using UnityEngine;

public class FallingSandEntity : MonoBehaviour
{
    public VoxelWorld world;

    Rigidbody rb;
    bool placed;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (placed || world == null) return;

        Vector3 pos = transform.position;
        Vector3Int cell = new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));

        Vector3Int below = new Vector3Int(cell.x, cell.y - 1, cell.z);
        if (below.y < 0) return;

        BlockType b = world.GetBlock(below);
        if (b == BlockType.Air || b == BlockType.Water) return;

        Vector3Int place = cell;
        if (place.y < 0 || place.y >= VoxelData.ChunkHeight) return;

        if (world.GetBlock(place) != BlockType.Air) return;

        placed = true;
        world.SetBlock(place, BlockType.Sand);
        Destroy(gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (placed || world == null) return;

        Vector3 pos = transform.position;
        Vector3Int cell = new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));

        Vector3Int below = new Vector3Int(cell.x, cell.y - 1, cell.z);
        if (below.y < 0) return;

        BlockType b = world.GetBlock(below);
        if (b == BlockType.Air || b == BlockType.Water) return;

        Vector3Int place = cell;
        if (place.y < 0 || place.y >= VoxelData.ChunkHeight) return;

        if (world.GetBlock(place) != BlockType.Air) return;

        placed = true;
        world.SetBlock(place, BlockType.Sand);
        Destroy(gameObject);
    }
}
