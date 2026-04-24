using UnityEngine;

public class BlendShapeIndexLogger : MonoBehaviour
{
    [SerializeField] private SkinnedMeshRenderer faceRenderer;

    [ContextMenu("Log BlendShape Indices")]
    private void LogBlendShapeIndices()
    {
        if (faceRenderer == null)
        {
            Debug.LogWarning("BlendShapeIndexLogger: falta el SkinnedMeshRenderer.");
            return;
        }

        if (faceRenderer.sharedMesh == null)
        {
            Debug.LogWarning("BlendShapeIndexLogger: el SkinnedMeshRenderer no tiene sharedMesh.");
            return;
        }

        Mesh mesh = faceRenderer.sharedMesh;
        int count = mesh.blendShapeCount;

        Debug.Log($"BlendShape count: {count}", this);

        for (int i = 0; i < count; i++)
        {
            Debug.Log($"[{i}] {mesh.GetBlendShapeName(i)}", this);
        }
    }
}