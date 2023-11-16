using p3ppc.expShare.NuGet.templates.defaultPlus;
using Reloaded.Hooks.ReloadedII.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace p3ppc.expShare;
internal static unsafe class Native
{
    internal static GetPartyMemberPersonaDelegate GetPartyMemberPersona;
    internal static CalculateGainedExpDelegate CalculateGainedExp;
    internal static GetPersonaRequiredExpDelegate GetPersonaRequiredExp;
    internal static GenerateLevelUpPersonaDelegate GenerateLevelUpPersona;
    internal static CanPersonaLevelUpDelegate CanPersonaLevelUp;
    internal static LevelUpPersonaDelegate LevelUpPersona;
    internal static GetAvailablePartyDelegate GetAvailableParty;
    internal static GetProtagPersonaDelegate GetProtagPersona;

    internal static void Initialise(IReloadedHooks hooks)
    {
        Utils.SigScan("40 53 48 83 EC 20 0F B7 D9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 66 83 FB 01 75 ?? 0F B7 0D ?? ?? ?? ??", "GetPartyMemberPersona", address =>
        {
            GetPartyMemberPersona = hooks.CreateWrapper<GetPartyMemberPersonaDelegate>(address, out _);
        });

        Utils.SigScan("48 89 5C 24 ?? 57 48 83 EC 20 89 CF 48 89 D3 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 53 ??", "CalculateGainedExp", address =>
        {
            CalculateGainedExp = hooks.CreateWrapper<CalculateGainedExpDelegate>(address, out _);
        });
        Utils.SigScan("E8 ?? ?? ?? ?? BB C0 00 00 00 41 3B C7", "GetPersonaRequiredExpPtr", address =>
        {
            var funcAddress = Utils.GetGlobalAddress(address + 1);
            Utils.LogDebug($"Found GetPersonaRequiredExp at 0x{funcAddress:X}");
            GetPersonaRequiredExp = hooks.CreateWrapper<GetPersonaRequiredExpDelegate>((long)funcAddress, out _);
        });

        Utils.SigScan("48 89 5C 24 ?? 48 89 54 24 ?? 48 89 4C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 83 EC 30", "GenerateLevelUpPersona", address =>
        {
            GenerateLevelUpPersona = hooks.CreateWrapper<GenerateLevelUpPersonaDelegate>(address, out _);
        });

        Utils.SigScan("E8 ?? ?? ?? ?? 85 C0 74 ?? 48 89 FA", "CanPersonaLevelUpPtr", address =>
        {
            var funcAddress = Utils.GetGlobalAddress(address + 1);
            Utils.LogDebug($"Found CanPersonaLevelUp at 0x{funcAddress:X}");
            CanPersonaLevelUp = hooks.CreateWrapper<CanPersonaLevelUpDelegate>((long)funcAddress, out _);
        });

        Utils.SigScan("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 89 CB 48 89 D7 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B7 43 ??", "LevelUpPersona", address =>
        {
            LevelUpPersona = hooks.CreateWrapper<LevelUpPersonaDelegate>(address, out _);
        });

        Utils.SigScan("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 40 4C 8B F1 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? BD 01 00 00 00 45 33 FF 41 0F B7 F7", "GetAvailableParty", address =>
        {
            GetAvailableParty = hooks.CreateWrapper<GetAvailablePartyDelegate>(address, out _);
        });

        Utils.SigScan("40 53 48 83 EC 20 0F B7 D9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 66 85 DB 78 ?? E8 ?? ?? ?? ?? 0F B7 D0 0F BF C3 39 D0 7C ?? 8B 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? FF C2 E8 ?? ?? ?? ?? 48 0F BF C3 48 6B C8 34 48 8D 05 ?? ?? ?? ?? 48 01 C8", "GetProtagPersona", address =>
        {
            GetProtagPersona = hooks.CreateWrapper<GetProtagPersonaDelegate>(address, out _);
        });

    }

    internal delegate Persona* GetPartyMemberPersonaDelegate(PartyMember member);
    internal delegate int CalculateGainedExpDelegate(int level, astruct_2* param_2);
    internal delegate int GetPersonaRequiredExpDelegate(Persona* persona, ushort level);
    internal delegate void GenerateLevelUpPersonaDelegate(Persona* persona, PersonaStatChanges* changes, int gainedExp);
    internal delegate nuint CanPersonaLevelUpDelegate(Persona* persona, nuint expGained, nuint param_3, nuint param_4);
    internal delegate void LevelUpPersonaDelegate(Persona* persona, PersonaStatChanges* personaStatChanges);
    internal delegate short GetAvailablePartyDelegate(short* party);
    internal delegate Persona* GetProtagPersonaDelegate(short slot);

    [StructLayout(LayoutKind.Explicit)]
    internal struct Persona
    {
        [FieldOffset(0)]
        internal bool IsRegistered;

        [FieldOffset(2)]
        internal short Id;

        [FieldOffset(4)]
        internal byte Level;

        [FieldOffset(8)]
        internal int Exp;

        [FieldOffset(12)]
        internal fixed short Skils[8];

        [FieldOffset(0x1C)]
        internal PersonaStats Stats;

        [FieldOffset(0x21)]
        internal PersonaStats BonusStats;

        [FieldOffset(0x33)]
        byte unk; // just to make it 0x34 long
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PersonaStatChanges
    {
        [FieldOffset(0)]
        internal byte LevelIncrease;

        [FieldOffset(0x87)]
        byte unk; // Just to make it the right length
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct astruct_2
    {
        [FieldOffset(0x16)]
        internal fixed short PartyMembers[4];

        [FieldOffset(0x20)]
        internal uint NumPartyMembers;

        [FieldOffset(0x3c)]
        int unk; // Just to make the struct the right length
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PersonaStats
    {
        internal byte Strength;
        internal byte Magic;
        internal byte Endurance;
        internal byte Agility;
        internal byte Luck;
    }

    internal enum PartyMember : short
    {
        None,
        Protag,
        Yukari,
        Aigis,
        Mitsuru,
        Junpei,
        Fuuka,
        Akihiko,
        Ken,
        Shinjiro,
        Koromaru
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct BattleResults
    {
        [FieldOffset(0)]
        internal int LevelUpStatus;

        [FieldOffset(4)]
        internal int GainedExp;

        [FieldOffset(8)]
        internal fixed uint ProtagExpGains[12];

        [FieldOffset(0x38)]
        internal PersonaStatChanges ProtagPersonaChanges; // This is an array of 12

        [FieldOffset(0x69A)]
        internal fixed short PartyMembers[4];

        [FieldOffset(0x6A4)]
        internal fixed uint ExpGains[4];

        [FieldOffset(0x6B4)]
        internal PersonaStatChanges PersonaChanges; // This is an array of 4
    }

    // I know, these are really good names. I'm just not sure exactly what these structs are about 
    [StructLayout(LayoutKind.Explicit)]
    internal struct BattleResultsThing
    {
        [FieldOffset(0x48)]
        internal BattleResultsThing2* Thing;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct BattleResultsThing2
    {
        [FieldOffset(4)]
        internal int State;

        [FieldOffset(0x68)]
        internal int LevelUpSlot;

        [FieldOffset(0x70)]
        internal astruct_3* Info;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct astruct_3
    {
        [FieldOffset(0xc8)]
        internal BattleResults Results;
    }
}