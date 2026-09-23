using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class M5eReadabilitySetup
    {
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
            GameObject root = GameObject.Find("[Presentation] Bridge Drop Edges");
            if (root == null) root = new GameObject("[Presentation] Bridge Drop Edges");
            while (root.transform.childCount > 0) Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/M6Art/M_ArenaBoundaryStone.mat");
            const string path = "Assets/_Game/Art/Materials/M6Art/M_DropEdgeAmber.mat";
            var amber = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (amber == null)
            {
                amber = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                amber.color = new Color(0.85f, 0.46f, 0.12f);
                amber.EnableKeyword("_EMISSION");
                amber.SetColor("_EmissionColor", new Color(0.18f, 0.07f, 0.01f));
                AssetDatabase.CreateAsset(amber, path);
            }
            // Only exposed north/south deck edges; east/west connections remain untouched.
            foreach (float z in new[] { 39.4f, 50.2f })
            {
                Cube(root.transform, "DeepBridgeCut", new Vector3(13f, -3.2f, z), new Vector3(8.5f, 6f, 0.16f), stone);
                foreach (float x in new[] { 9.3f, 11.8f, 14.3f, 16.7f })
                    Cube(root.transform, "AmberDropWarning", new Vector3(x, 0.10f, z), new Vector3(0.7f, 0.16f, 0.22f), amber);
            }
            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("READABILITY_SETUP_COMPLETE version=0.8.10b collisionChanges=0");
        }

        private static void Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            item.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
