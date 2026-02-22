using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LoadingRotate : MonoBehaviour
{
    [Header("旋转速度 (度/秒)")]
    [SerializeField] private float rotateSpeed = -360f; // 负数代表顺时针

    void Update()
    {
        // 绕 Z 轴旋转
        transform.Rotate(0, 0, rotateSpeed * Time.deltaTime);
    }
}
