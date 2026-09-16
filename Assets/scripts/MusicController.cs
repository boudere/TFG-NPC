using UnityEngine;

public class MusicController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    
    private AudioSource musica;

    void Start()
    {
        musica = GetComponent<AudioSource>();

        if (InfoCharacter.music)
            musica.Play();
        else
            musica.Stop();
    }
}
