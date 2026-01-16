using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BlockOutline : MonoBehaviour
{
    public float lineWidth = 0.02f;

    private LineRenderer lr;

    private static readonly Vector3[] pts = new Vector3[]
    {
        new Vector3(0,0,0), new Vector3(1,0,0),
        new Vector3(1,0,0), new Vector3(1,0,1),
        new Vector3(1,0,1), new Vector3(0,0,1),
        new Vector3(0,0,1), new Vector3(0,0,0),

        new Vector3(0,1,0), new Vector3(1,1,0),
        new Vector3(1,1,0), new Vector3(1,1,1),
        new Vector3(1,1,1), new Vector3(0,1,1),
        new Vector3(0,1,1), new Vector3(0,1,0),

        new Vector3(0,0,0), new Vector3(0,1,0),
        new Vector3(1,0,0), new Vector3(1,1,0),
        new Vector3(1,0,1), new Vector3(1,1,1),
        new Vector3(0,0,1), new Vector3(0,1,1),
    };

    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.loop = false;
        lr.useWorldSpace = true;
        lr.positionCount = pts.Length;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        gameObject.SetActive(false);
    }

    public void Show(Vector3Int blockPos)
    {
        for (int i = 0; i < pts.Length; i++)
            lr.SetPosition(i, (Vector3)blockPos + pts[i]);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
