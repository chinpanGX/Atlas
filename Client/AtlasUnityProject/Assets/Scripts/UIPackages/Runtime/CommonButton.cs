using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UIPackages.Runtime
{
    public sealed class CommonButton : Button
    {
        [Space(10)] [Header(" ColorTint のときに、色を同期するグラフィックのリスト")] [SerializeField]
        internal List<Graphic> synchronizeTintColorGraphics = new();
        
        private TextMeshProUGUI cachedLabel;

        /// <summary>
        /// ボタンのテキストを設定します。
        /// </summary>
        /// <param name="text"> 設定するテキスト </param>
        /// <remarks>
        /// ボタンの子オブジェクトにある TextMeshProUGUI コンポーネントを検索し、
        /// 1つのみ見つかった場合にそのテキストを設定します。
        /// TextMeshProUGUI コンポーネントが見つからない場合, エラーメッセージを表示します。
        /// 複数の TextMeshProUGUI コンポーネントが見つかった場合は、エラーメッセージを表示します。
        /// </remarks>
        public void SetTextInChildSafe(string text)
        {
            if (cachedLabel == null)
            {
                var comps = GetComponentsInChildren<TextMeshProUGUI>();
                switch (comps.Length)
                {
                    case 0:
                        Debug.LogError("TextMeshProUGUI component is missing on " + gameObject.name);
                        return;
                    case > 1:
                        Debug.LogError("Multiple TextMeshProUGUI components found on " + gameObject.name +
                                       ". Please ensure only one exists."
                        );
                        return;
                    default:
                        cachedLabel = comps[0];
                        break;
                }
            }
            
            cachedLabel.text = text;
        }
        
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);

            var tintColor = state switch
            {
                SelectionState.Normal => colors.normalColor,
                SelectionState.Highlighted => colors.highlightedColor,
                SelectionState.Pressed => colors.pressedColor,
                SelectionState.Selected => colors.selectedColor,
                SelectionState.Disabled => colors.disabledColor,
                _ => Color.black
            };

            switch (transition)
            {
                case Transition.ColorTint:
                    if (synchronizeTintColorGraphics.Count == 0)
                        return;
                    foreach (var graphics in synchronizeTintColorGraphics)
                    {
                        if (graphics == null)
                            continue;

                        graphics.CrossFadeColor(tintColor * colors.colorMultiplier,
                            instant ? 0f : colors.fadeDuration, true, true
                        );
                    }
                    break;
                case Transition.None:
                case Transition.SpriteSwap:
                case Transition.Animation:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

}
