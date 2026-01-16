using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BlockOutlineMesh : MonoBehaviour
{
    public float inset = 0.002f;
    public Color color = Color.black;

    private MeshFilter mf;
    private MeshRenderer mr;

    private Mesh mesh;
    private Vector3[] verts;
    private int[] indices;

    private static readonly Vector3[] unit = new Vector3[]
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
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();

        Shader sh = Shader.Find("Custom/OutlineVisible");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");

        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mr.sharedMaterial = mat;

        mesh = new Mesh();
        mesh.name = "OutlineLines";
        mesh.MarkDynamic();

        verts = new Vector3[unit.Length];
        indices = new int[unit.Length];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;

        mf.sharedMesh = mesh;

        gameObject.SetActive(false);
    }

    public void Show(Vector3Int blockPos)
    {
        Vector3 basePos = (Vector3)blockPos + new Vector3(-inset, -inset, -inset);
        Vector3 scale = new Vector3(1f + inset * 2f, 1f + inset * 2f, 1f + inset * 2f);

        for (int i = 0; i < unit.Length; i++)
            verts[i] = basePos + Vector3.Scale(unit[i], scale);

        mesh.Clear(false);
        mesh.SetVertices(verts);
        mesh.SetIndices(indices, MeshTopology.Lines, 0, true);
        mesh.RecalculateBounds();

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }
}
