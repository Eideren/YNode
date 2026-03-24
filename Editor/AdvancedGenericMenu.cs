#if UNITY_2019_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using static UnityEditor.GenericMenu;

namespace YNode.Editor
{
    public class AdvancedGenericMenu : AdvancedDropdown
    {
        public static float? DefaultMinWidth = 200f;
        public static float? DefaultMaxWidth = 300f;

        private readonly string _name;
        private readonly List<AdvancedGenericMenuItem?> _items = new();


        public AdvancedGenericMenu(string name = "", AdvancedDropdownState? state = null) : base(state ?? new AdvancedDropdownState())
        {
            _name = name;
        }

        public void AddDisabledItem(string text, Texture2D? image = null)
        {
            //var parent = FindParent( content.text );
            var item = FindOrCreateItem(text);
            item.Set(false, image);
        }

        public void AddItem(string text, MenuFunction func, Texture2D? image = null)
        {
            //var parent = FindParent( content.text );
            var item = FindOrCreateItem(text);
            item.Set(true, image, func);
        }

        //
        // Summary:
        //     Add a seperator item to the menu.
        //
        // Parameters:
        //   path:
        //     The path to the submenu, if adding a separator to a submenu. When adding a separator
        //     to the top level of a menu, use an empty string as the path.
        public void AddSeparator(string? path = null)
        {
            var parent = path == null ? null : FindParent(path);
            if (parent == null)
                _items.Add(null);
            else
                parent.AddSeparator();
        }

        //
        // Summary:
        //     Show the menu at the given screen rect.
        //
        // Parameters:
        //   position:
        //     The position at which to show the menu.
        public void DropDown(Rect position)
        {
            position.width = Mathf.Clamp(position.width, DefaultMinWidth.HasValue ? DefaultMinWidth.Value : 1f,
                DefaultMaxWidth.HasValue ? DefaultMaxWidth.Value : Screen.width);

            Show(position);
            // Hide header bar from the drop down
            if (GetType().GetField("m_WindowInstance", BindingFlags.NonPublic | BindingFlags.Instance) is {} windowField
                && windowField.GetValue(this) is {} window
                && window.GetType().GetProperty("showHeader", BindingFlags.NonPublic | BindingFlags.Instance) is {} showHeaderProp)
            {
                showHeaderProp.SetValue(window, false);
            }
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(_name);

            foreach (var m in _items)
            {
                if (m == null)
                {
                    root.AddSeparator();
                    continue;
                }

                RootDirectories(m, root, 0);
            }

            return root;

            static void RootDirectories(AdvancedDropdownItem item, AdvancedDropdownItem root, int rec)
            {
                if (item.children.Any())
                {
                    root.AddChild(new AdvancedDropdownItem($"{new string(' ', rec*2)}{item.name}"){ enabled = false, icon = EditorGUIUtility.IconContent("d_Toolbar Minus").image as Texture2D });
                    foreach (var advancedDropdownItem in item.children)
                    {
                        RootDirectories(advancedDropdownItem, root, rec+1);
                    }
                    root.AddSeparator();
                }
                else
                {
                    root.AddChild(item);
                }
            }
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is AdvancedGenericMenuItem gmItem)
            {
                try
                {
                    Event.current.Use(); // We right-clicked an option, make sure the event is consumed
                    gmItem.Run();
                }
                catch (Exception e) // We have to introduce this here, otherwise the error does not get surfaced within Unity
                {
                    Debug.LogException(e);
                }
            }
        }

        private AdvancedGenericMenuItem FindOrCreateItem(string name, AdvancedGenericMenuItem? currentRoot = null)
        {
            AdvancedGenericMenuItem? item;

            string[] paths = name.Split('/');
            if (currentRoot == null)
            {
                item = _items.FirstOrDefault(x => x != null && x.name == paths[0]);
                if (item == null)
                    _items.Add(item = new AdvancedGenericMenuItem(paths[0]));
            }
            else
            {
                item = currentRoot.children.OfType<AdvancedGenericMenuItem>().FirstOrDefault(x => x.name == paths[0]);
                if (item == null)
                    currentRoot.AddChild(item = new AdvancedGenericMenuItem(paths[0]));
            }

            if (paths.Length > 1)
                return FindOrCreateItem(string.Join("/", paths, 1, paths.Length - 1), item);

            return item;
        }

        private AdvancedGenericMenuItem FindParent(string name)
        {
            string[] paths = name.Split('/');
            return FindOrCreateItem(string.Join("/", paths, 0, paths.Length - 1));
        }

        private class AdvancedGenericMenuItem : AdvancedDropdownItem
        {
            private MenuFunction? _func;

            public void Set(bool enabled, Texture2D? icon = null, MenuFunction? func2 = null)
            {
                this.enabled = enabled;
                this.icon = icon;
                _func = func2;
            }

            public void Run()
            {
                _func?.Invoke();
            }

            public AdvancedGenericMenuItem(string name) : base(name)
            {
            }
        }
    }
}
#endif
