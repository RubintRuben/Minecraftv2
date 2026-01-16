using UnityEngine;
using UnityEngine.InputSystem;

public class BlockInteractor : MonoBehaviour
{
    public VoxelWorld world;
    public float reach = 6f;

    [Header("Place")]
    public BlockType placeType = BlockType.Dirt;

    [Header("Outline")]
    public BlockOutline outline;

    [Header("Raycast")]
    public LayerMask hitMask = ~0; 

    private Camera cam;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (world == null || cam == null) return;
        if (Mouse.current == null) return;

        bool hasHit = Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, reach, hitMask);

        if (hasHit)
        {
           
            Vector3 inside = hit.point - hit.normal * 0.01f; 
            Vector3Int targetBlock = new Vector3Int(
                Mathf.FloorToInt(inside.x),
                Mathf.FloorToInt(inside.y),
                Mathf.FloorToInt(inside.z)
            );

            if (outline != null)
                outline.Show(targetBlock);

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                world.SetBlock(targetBlock, BlockType.Air);
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                Vector3 place = hit.point + hit.normal * 0.01f;
                Vector3Int placeBlock = new Vector3Int(
                    Mathf.FloorToInt(place.x),
                    Mathf.FloorToInt(place.y),
                    Mathf.FloorToInt(place.z)
                );

                if (world.GetBlock(placeBlock) == BlockType.Air)
                    world.SetBlock(placeBlock, placeType);
            }
        }
        else
        {
            if (outline != null)
                outline.Hide();
        }
    }
}
