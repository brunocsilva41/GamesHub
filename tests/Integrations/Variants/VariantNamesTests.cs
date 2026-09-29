using System;

namespace GamesHub.Tests
{
    public static class VariantNamesTests
    {
        // name, expected base, expected qualifier
        private static readonly string[,] Table =
        {
            { "LEGO Marvel Super Heroes 2", "LEGO Marvel Super Heroes 2", "" },
            { "LEGO Marvel Super Heroes 2 DirectX 11", "LEGO Marvel Super Heroes 2", "DirectX 11" },
            { "Game DX12", "Game", "DirectX 12" },
            { "Game - DirectX 9", "Game", "DirectX 9" },
            { "Game (DX11)", "Game", "DirectX 11" },
            { "Game [Vulkan]", "Game", "Vulkan" },
            { "Game OpenGL", "Game", "OpenGL" },
            { "Game 64-bit", "Game", "64-bit" },
            { "Game 32 bit", "Game", "32-bit" },
            { "Game x64", "Game", "x64" },
            { "Game (Safe Mode)", "Game", "Modo seguro" },
            { "Game Launcher", "Game", "Launcher" },
            { "Game Multiplayer", "Game", "Multijogador" },
            { "Game - MP", "Game", "Multijogador" },
            { "Game SP", "Game", "Um jogador" },
            { "Call of Duty Black Ops 3 Zombies", "Call of Duty Black Ops 3", "Zombies" },
            { "Half-Life 2 VR", "Half-Life 2", "VR" },
            { "Game Beta", "Game", "Beta" },
            { "Game Legacy", "Game", "Legacy" },
            { "Game Classic", "Game", "Clássico" },
            { "PointBlank.exe - Atalho", "PointBlank", "" },
            { "Hot Wheels Unleashed 2.exe", "Hot Wheels Unleashed 2", "" },
            { "TEKKEN 7.exe", "TEKKEN 7", "" },
            { "DIRT 5 (Install Crack)", "DIRT 5", "" },
            { "Game (Old Build)", "Game", "" },
            { "Game (Remastered)", "Game", "Remastered" },
            { "Game DX11 Launcher", "Game", "DirectX 11 Launcher" },
            { "Roblox Player", "Roblox Player", "" },
            { "Roblox Studio", "Roblox Studio", "" },
            { "Counter-Strike 2", "Counter-Strike 2", "" },
            { "SKlauncher", "SKlauncher", "" },
            { "Launcher", "Launcher", "" },
            { "Plants vs. Zombies", "Plants vs. Zombies", "" },
            { "Epic Games Launcher", "Epic Games", "Launcher" },
            { "Call of Duty - Black Ops 3", "Call of Duty - Black Ops 3", "" },
        };

        public static void TestQualifierTable()
        {
            for (int i = 0; i < Table.GetLength(0); i++)
            {
                string name = Table[i, 0];
                Assert.Equal(Table[i, 1], VariantService.BaseName(name), "base of '" + name + "'");
                Assert.Equal(Table[i, 2], VariantService.Qualifier(name), "qualifier of '" + name + "'");
            }
        }

        public static void TestNormalizeIgnoresCaseAccentsPunctuation()
        {
            Assert.Equal(VariantNames.Normalize("Pokémon: Édition"), VariantNames.Normalize("pokemon edition"));
            Assert.Equal(VariantNames.Normalize("Tom Clancy's Rainbow Six® Siege"), VariantNames.Normalize("tom clancys rainbow six siege"));
            Assert.Equal("", VariantNames.Normalize(null));
        }

        public static void TestNullSafe()
        {
            Assert.Equal("", VariantService.BaseName(null));
            Assert.Equal("", VariantService.Qualifier(""));
        }
    }
}
