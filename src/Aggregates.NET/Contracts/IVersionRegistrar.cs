﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IVersionRegistrar
    {
        void Load(Type[] types);

        string GetVersionedName(Type versionedType);
        Type GetNamedType(string versionedName);
    }
}
