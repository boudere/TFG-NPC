using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader instance;

    private AsyncOperation _async;

    private void Awake()
    {
        instance = this;
    }

    //public void LoadSceneAsync(string scene)
    //{
    //    StartCoroutine(AsyncLoading(scene));
    //}

    //private IEnumerator AsyncLoading(string scene)
    //{
    //    _async = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);

    //    while (!_async.isDone)
    //    {
    //        yield return null;
    //    }
    //}
}
