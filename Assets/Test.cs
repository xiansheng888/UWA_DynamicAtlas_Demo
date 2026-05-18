using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour
{

    public UIDynamicImage img;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            img.SetImage("test", () =>
            {
                Debug.Log("╪стьмЙЁи");
            });
        }
    }
}
