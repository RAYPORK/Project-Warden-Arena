using UnityEngine;

/// <summary>腳下／鉤索表面材質（與地面移動與鋼索邏輯共用）。</summary>
public enum MaterialType
{
    Concrete,
    Lava,
    Ice,
}

/// <summary>可立足或可鉤平台的材質標記；掛在平台根或碰撞器父階層。</summary>
public class PlatformType : MonoBehaviour
{
    public MaterialType type = MaterialType.Concrete;
}
