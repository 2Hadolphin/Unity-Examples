using Sirenix.Utilities;
using System;


namespace Return.Editors
{
    /// <summary>
    /// Options for unity package that contains project datas.
    /// </summary>
    [Flags]
    public enum ExportProjectOption
    {
        None = 0,
        Tags = 1 << 0,
        Layers = 1 << 1,
        Inputs = 1 << 2,
        Graphics = 1<<3,
        Packages =1<<4,
        Physics = 1<<5,

        All = Tags | Layers | Inputs | Graphics | Packages | Physics
    }
}