using System;

/// <summary>
///     Symphony Frameworkが自動生成したフラグ列挙型。
/// </summary>
[Flags]
public enum LayersEnum : int
{
    /// <summary> Noneを表す。 </summary>
    None = 1 << 0,
    /// <summary> Defaultを表す。 </summary>
    Default = 1 << 1,
    /// <summary> TransparentFXを表す。 </summary>
    TransparentFX = 1 << 2,
    /// <summary> Waterを表す。 </summary>
    Water = 1 << 3,
    /// <summary> UIを表す。 </summary>
    UI = 1 << 4,
}
