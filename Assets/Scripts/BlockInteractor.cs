using UnityEngine;
using UnityEngine.InputSystem;

public class BlockInteractor : MonoBehaviour
{
    public VoxelWorld world;
    public float reach = 6f;

    [Header("Place")]
    public BlockType placeType = BlockType.Dirt;

    [Header("Refs")]
    public BlockOutlineMesh outline;
    public PlayerController playerController;

    [Header("Raycast")]
    public LayerMask hitMask = ~0; 

    private Camera cam;

    private static readonly RaycastHit[] hitBuf = new RaycastHit[8];

    private Vector3Int lastOutlined;
    private bool hadOutline;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    private void Update()
    {
        if (world == null || cam == null || Mouse.current == null) return;

        int mask = hitMask;
        if (playerController != null)
            mask &= ~(1 << playerController.gameObject.layer);

        Ray r = new Ray(cam.transform.position, cam.transform.forward);
        int hitCount = Physics.RaycastNonAlloc(r, hitBuf, reach, mask, QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
        {
            if (outline != null) outline.Hide();
            hadOutline = false;
            return;
        }

        int bestI = -1;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            float d = hitBuf[i].distance;
            if (d < bestD)
            {
                bestD = d;
                bestI = i;
            }
        }

        if (bestI < 0)
        {
            if (outline != null) outline.Hide();
            hadOutline = false;
            return;
        }

        RaycastHit hit = hitBuf[bestI];

        Vector3 inside = hit.point - hit.normal * 0.01f;
        Vector3Int targetBlock = new Vector3Int(
            Mathf.FloorToInt(inside.x),
            Mathf.FloorToInt(inside.y),
            Mathf.FloorToInt(inside.z)
        );

        if (world.GetBlock(targetBlock) == BlockType.Air)
        {
            if (outline != null) outline.Hide();
            hadOutline = false;
            return;
        }

        if (outline != null)
        {
            if (!hadOutline || targetBlock != lastOutlined)
            {
                outline.Show(targetBlock);
                lastOutlined = targetBlock;
                hadOutline = true;
            }
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (world.GetBlock(targetBlock) != BlockType.Air)
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

            if (world.GetBlock(placeBlock) != BlockType.Air) return;

            if (playerController != null && playerController.IntersectsBlock(placeBlock))
                return;

            world.SetBlock(placeBlock, placeType);
        }
    }
}
