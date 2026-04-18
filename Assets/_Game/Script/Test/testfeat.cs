using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class testfeat : MonoBehaviour
{
    void Start()
    {
        // Di chuyển đến x = 5 trong 2 giây
        transform.DOMoveX(5f, 2f)
            .SetEase(Ease.Linear)      // Di chuyển đều (không nhanh chậm)
            .SetLoops(-1, LoopType.Yoyo); // -1 là lặp vô tận, Yoyo là đi rồi quay lại
    }
}
