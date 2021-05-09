using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;

namespace SpeechToTextWithAmiVoice
{
    /// <summary>
    /// Viewboxをいじったやつ
    /// </summary>
    /// <seealso cref="https://ja.stackoverflow.com/questions/7670/"/>
    class ViewboxEx : Viewbox
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var child = this.Child as Layoutable;
            if (child == null)
            {
                return base.MeasureOverride(availableSize);
            }
            child.Width = double.NaN;
            child.Height = double.NaN;
            child.ClearValue(Layoutable.MinWidthProperty);
            child.ClearValue(Layoutable.MaxWidthProperty);
            var sz = base.MeasureOverride(availableSize);
            if (sz.Width == 0 || sz.Height == 0)
            {

            }
            else
            {
                Size csz = Child.DesiredSize;
                double thisRatio = availableSize.Width / availableSize.Height;
                double childRatio = child.DesiredSize.Width / child.DesiredSize.Height;
                if (childRatio != thisRatio)
                {
                    double div = 1;
                    child.Width = child.DesiredSize.Height * thisRatio;
                    child.Height = double.NaN;
                    sz = base.MeasureOverride(availableSize);
                    for (int i = 0; i < 10; i++)
                    {
                        childRatio = child.DesiredSize.Width / child.DesiredSize.Height;
                        if (childRatio < thisRatio)
                        {
                            child.Width = child.DesiredSize.Width + csz.Width / div;
                        }
                        else if (childRatio > thisRatio)
                        {
                            child.Width = Math.Max(0, child.DesiredSize.Width - csz.Width / div);
                        }
                        else if (childRatio == thisRatio)
                        {
                            break;
                        }
                        sz = base.MeasureOverride(availableSize);
                        div *= 2;
                    }
                }
            }
            return sz;
        }
    }
}
