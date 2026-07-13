// Copyright (c) 2026 Nenkai
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;

namespace GraniteLib.TileSet;

public struct TileInfo
{
    public int FlatTileIndex;

    public TileInfo(int flatTileIndex)
    {
        FlatTileIndex = flatTileIndex;
    }
}
