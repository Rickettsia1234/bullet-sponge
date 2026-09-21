using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public struct SceneDataPair
{
    public SceneName key;
#if UNITY_EDITOR
    public SceneAsset sceneAsset;
#endif
    public string sceneName;
}

public class SceneController : MonoBehaviour
{
    [SerializeField] private List<SceneDataPair> SceneData;
    private readonly Dictionary<SceneName, string> sceneDictionary = new();

    private void Awake()
    {
        foreach (var pair in SceneData)
        {
            sceneDictionary[pair.key] = pair.sceneName;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        for (int i = 0; i < SceneData.Count; i++)
        {
            var pair = SceneData[i];
            if (pair.sceneAsset != null)
            {
                pair.sceneName = pair.sceneAsset.name;
                SceneData[i] = pair;
            }
        }
    }
#endif

    public void ChangeScene(SceneName sceneName)
    {
        SceneManager.LoadScene(sceneDictionary[sceneName]);
    }
}