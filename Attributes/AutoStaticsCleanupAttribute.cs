using System;

namespace Unity.Scripting.LifecycleManagement
{
#if UNITY_6000_0_OR_NEWER
#else
    public class AutoStaticsCleanupAttribute : Attribute
    {

    }

    public class NoAutoStaticsCleanupAttribute : Attribute
    {
    }
#endif
}
