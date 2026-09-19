using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;

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
    public AudioClip lanternThrow;
    public AudioClip lanternBreak;
    public AudioClip lanternBurn;
    public AudioClip lightning;
    public AudioClip bow;
    public AudioClip arrow;
    public AudioClip shield;
    public AudioClip sniper;
    public AudioClip daggerThrow;
    public AudioClip daggerHit;
    public AudioClip scythe;
    public AudioClip scytheHit;

    void Awake()
    {
        instance = this;
    }
    //Play realworld BGM at start of game
    private void Start()
    {
        musicSource.clip = realworld;
        musicSource.loop = true;
        musicSource.Play();
    }

    //Play SFX
    public void PlaySFX(AudioClip clip, float volume)
    {
        SFXSource.PlayOneShot(clip, volume);
    }

    //Play SFX RandomPitch
    public void PlaySFXPitch(AudioClip clip, float volume)
    {
        SFXSource.pitch = Random.Range(0.8f, 1.2f);
        SFXSource.PlayOneShot(clip, volume);
    }
}
