using UnityEngine;
using System.Collections;

public class BossIncoming : MonoBehaviour
{
    //Banner including both image and text
    public RectTransform bossBanner;

    //Banner position above screen
    public float startY = 770f;

    //Position when the banner pauses
    public float endY = 260f;

    //Length of the floating animation
    public float moveDuration = 2f;

    public void ShowBossIncoming()
    {
        StartCoroutine(MoveBannerDown());
    }


    private IEnumerator MoveBannerDown()
    {
        float timer = 0f;

        //Banner's current anchored position
        Vector2 startPosition = bossBanner.anchoredPosition;
        startPosition.y = startY;

        //Banner's end position
        Vector2 endPosition = startPosition;
        endPosition.y = endY;

        //Start Positon of the banner above screen
        bossBanner.anchoredPosition = startPosition;

        //Transition from start to end position
        while (timer < moveDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / moveDuration;
            bossBanner.anchoredPosition = Vector2.Lerp(startPosition, endPosition, progress);
            
            yield return null;
        }

        //End the position of the banner at endY position
        bossBanner.anchoredPosition = endPosition;
    }

}
