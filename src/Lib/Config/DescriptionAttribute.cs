using System;

namespace DeathCorpses.Lib.Config
{
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DescriptionAttribute : Attribute
    {
        public string Text { get; }

        public DescriptionAttribute(string text)
        {
            Text = text;
        }
    }
}
