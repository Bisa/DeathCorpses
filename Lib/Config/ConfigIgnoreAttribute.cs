using System;

namespace DeathCorpses.Lib.Config
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ConfigIgnoreAttribute : Attribute { }
}
