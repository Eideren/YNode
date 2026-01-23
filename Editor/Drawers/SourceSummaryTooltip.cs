
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace YNode.Editor
{
    [DrawerPriority(DrawerPriorityLevel.SuperPriority)]
    public sealed class SummaryTooltipDrawer : OdinDrawer
    {
        private bool? _hasAttribute;

        public override bool CanDrawProperty(InspectorProperty property)
        {
            if (base.CanDrawProperty(property) && property.Info.GetMemberInfo() is { } memberInfo)
            {
                if (TryGetSummary(memberInfo, out _))
                    return true;

                return property.Attributes.Any(x => x.GetType() == typeof(TooltipAttribute));
            }

            return false;
        }

        protected override void DrawPropertyLayout(GUIContent? label)
        {
            var infoIconRect = GUILayoutUtility.GetLastRect();
            infoIconRect.y += infoIconRect.height + 2;
            infoIconRect.height = EditorGUIUtility.singleLineHeight;
            infoIconRect.x = 0;
            infoIconRect.width = EditorGUIUtility.singleLineHeight;

            bool hasSummary = false;
            if (Property.Info.GetMemberInfo() is { } memberInfo && TryGetSummary(memberInfo, out var summary))
            {
                hasSummary = true;
                if (label != null && string.IsNullOrEmpty(label.tooltip))
                    label.tooltip = summary;
            }

            CallNextDrawer(label);

            _hasAttribute ??= Property.Attributes.Any(x => x.GetType() == typeof(TooltipAttribute));

            if (hasSummary || _hasAttribute.Value)
            {
                var c = GUI.color;
                GUI.color *= new Color(1, 1, 1, 0.25f);
                GUI.DrawTexture(infoIconRect, EditorGUIUtility.IconContent("_Help@2x").image);
                GUI.color = c;
            }
        }

        private static bool TryGetSummary(MemberInfo member, out string str)
        {
            if (member.DeclaringType is null)
            {
                str = "";
                return false;
            }

            if (s_done == false)
            {
                str = "loading summaries ...";
                return true;
            }

            var key = (member.DeclaringType.FullName, member.Name);
            return s_summaries.TryGetValue(key, out str);
        }

        private static readonly Dictionary<(string typeName, string memberName), string> s_summaries = new();
        private static bool s_done;

        static SummaryTooltipDrawer()
        {
            Task.Run(FillXmlCache);

            static void FillXmlCache()
            {
                try
                {
                    var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                    var xmlsPath = Path.Combine(projectRoot, "Library/StreamingAssets/");

                    foreach (string xml in Directory.EnumerateFiles(xmlsPath, "*.xml", SearchOption.AllDirectories))
                    {
                        var doc = XDocument.Load(xml);

                        foreach (var member in doc.Descendants("member"))
                        {
                            var name = member.Attribute("name")?.Value;
                            var summary = member.Element("summary")?.Value?.Trim();

                            if (!string.IsNullOrEmpty(name)
                                && !string.IsNullOrEmpty(summary)
                                && Regex.Match(name, """(F|P)\:(?<typename>([^."]*\.)*)(?<membername>[^\"]+)""") is {} m && m.Success)
                            {
                                var type = m.Groups["typename"].ToString()[..^1];
                                var membername = m.Groups["membername"].ToString();
                                s_summaries[(type, membername)] = summary!;
                            }
                        }

                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    s_done = true;
                }
            }
        }
    }
}
