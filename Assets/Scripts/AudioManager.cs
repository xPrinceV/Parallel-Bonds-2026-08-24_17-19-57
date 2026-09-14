using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header ("--Audio Source--")]
    [SerializeField] AudioSource musicSource;
    [SerializeField] AudioSource SFXSource;

    [Header("--BGM--")]
    public AudioClip realworld;
    public AudioClip ruinedworld;
    public AudioClip bossmusic;
    public AudioClip victory;

    [Header("--Weapons SFX--")]
    public AudioClip jeffPistol;
    public AudioClip lantern;
    public AudioClip lightning;
    public AudioClip bow;
    public AudioClip aura;
    public AudioClip sniper;
    public AudioClip dagger;
    public AudioClip scythe;

    //Play realworld BGM at start of game
    private void Start()
    {
        musicSource.clip = realworld;
        musicSource.loop = true;
        musicSource.Play();
    }

    //Play SFX
    public void PlaySFX(AudioClip clip)
    {
        SFXSource.PlayOneShot(clip);
    }
}
