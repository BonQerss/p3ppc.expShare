using p3ppc.expShare.Configuration;
using p3ppc.expShare.NuGet.templates.defaultPlus;
using p3ppc.expShare.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using static p3ppc.expShare.Native;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace p3ppc.expShare;
/// <summary>
/// Your mod logic goes here.
/// </summary>
public unsafe class Mod : ModBase // <= Do not Remove.
{
    /// <summary>
    /// Provides access to the mod loader API.
    /// </summary>
    private readonly IModLoader _modLoader;

    /// <summary>
    /// Provides access to the Reloaded.Hooks API.
    /// </summary>
    /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
    private readonly IReloadedHooks? _hooks;

    /// <summary>
    /// Provides access to the Reloaded logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Entry point into the mod, instance that created this class.
    /// </summary>
    private readonly IMod _owner;

    /// <summary>
    /// Provides access to this mod's configuration.
    /// </summary>
    private Config _configuration;

    /// <summary>
    /// The configuration of the currently executing mod.
    /// </summary>
    private readonly IModConfig _modConfig;

    private IHook<SetupResultsExpDelegate> _setupExpHook;
    private IHook<GivePartyMemberExpDelegate> _givePartyMemberExpHook;
    private IHook<LevelUpPartyMemberDelegate> _levelUpPartyMemberHook;
    private delegate int GetTotalDayDelegate();
    private GetTotalDayDelegate _getTotalDay;

    private Dictionary<PartyMember, int> _expGains = new();
    private Dictionary<PartyMember, PersonaStatChanges> _levelUps = new();
    private short[] _available = new short[9];
    private int _numAvailable = 0;

    // Function delegates - based on actual assembly analysis
    private delegate void GiveProtagExpDelegate(IntPtr results, IntPtr param2, IntPtr param3, IntPtr param4);
    private delegate void GivePersonaExpDelegate(IntPtr persona, uint exp);
    private delegate IntPtr GetProtagPersonaDelegate(short slot);
    private delegate IntPtr GetPartyMemberPersonaDelegate(IntPtr partyMemberInfo);
    private delegate byte GetPersonaLevelDelegate(IntPtr persona);
    private delegate byte GetPartyMemberLevelDelegate(IntPtr partyMemberInfo);
    private delegate ushort GetNumPersonasDelegate();

    // Hooks
    private IHook<GiveProtagExpDelegate> _giveProtagExpHook;
    private IHook<GivePersonaExpDelegate> _givePersonaExpHook;

    // Function wrappers - based on actual assembly
    private GetProtagPersonaDelegate _getProtagPersona;
    private GetPartyMemberPersonaDelegate _getPartyMemberPersona;
    private GetPersonaLevelDelegate _getPersonaLevel;
    private GetPartyMemberLevelDelegate _getPartyMemberLevel;
    private GetNumPersonasDelegate _getNumPersonas;


    private readonly SortedDictionary<int, int> _levelCaps = new SortedDictionary<int, int>
        {
            { 0x26, 8 },   // May 9th
            { 0x44, 15 },   // June 8th
            { 0x61, 21 },   // July 7th 
            { 0x79, 32 },  // August 6th
            { 0x8F, 40 },  // September 5th
            { 0x9D, 46 },  // October 4th
            { 0xE1, 54 },  // November 3rd
            { 0xF4, 54 },  // November 22nd
            { 0x131, 76 }   // July 31st
        };

    private bool _isGivingExp = false;

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _owner = context.Owner;
        _configuration = context.Configuration;
        _modConfig = context.ModConfig;

        Utils.Initialise(_logger, _configuration, _modLoader);
        Native.Initialise(_hooks);

        Utils.SigScan("48 89 54 24 ?? 48 89 4C 24 ?? 53 55 56 57 41 54 41 55 48 83 EC 68", "SetupResultsExp", address =>
        {
            _setupExpHook = _hooks.CreateHook<SetupResultsExpDelegate>(SetupResultsExp, address).Activate();
        });

        Utils.SigScan("48 89 5C 24 ?? 48 89 6C 24 ?? 56 57 41 56 48 83 EC 20 48 89 CF 48 8D 0D ?? ?? ?? ??", "GivePartyMemberExp", address =>
        {
            _givePartyMemberExpHook = _hooks.CreateHook<GivePartyMemberExpDelegate>(GivePartyMemberExp, address).Activate();
        });

        Utils.SigScan("48 89 5C 24 ?? 48 89 6C 24 ?? 56 57 41 56 48 81 EC B0 00 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B E9 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B CD", "LevelUpPartyMember", address =>
        {
            _levelUpPartyMemberHook = _hooks.CreateHook<LevelUpPartyMemberDelegate>(LevelUpPartyMember, address).Activate();
        });

        // Get GetTotalDay function
        Utils.SigScan("E8 ?? ?? ?? ?? 0F BF C8 89 0D ?? ?? ?? ?? 89 74 24 ??", "GetTotalDay", address =>
        {
            var funcAddress = Utils.GetGlobalAddress((nint)(address + 1));
            _getTotalDay = _hooks.CreateWrapper<GetTotalDayDelegate>((long)funcAddress, out _);
            _logger.WriteLine($"Found GetTotalDay at 0x{funcAddress:X}");
        });

        Utils.SigScan("40 53 48 83 EC 20 48 89 CB 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B6 43 ?? 48 83 C4 20", "GetPersonaLevel", address =>
        {
            _getPersonaLevel = _hooks.CreateWrapper<GetPersonaLevelDelegate>(address, out _);
            _logger.WriteLine($"Found GetPersonaLevel at 0x{address:X}");
        });
    }

    private int GetCurrentLevelCap()
    {
        if (_getTotalDay == null)
        {
            _logger.WriteLine("[GetCurrentLevelCap] _getTotalDay delegate is null. Returning 99 (no cap).");
            return 99;
        }

        int currentDay = 0;
        try
        {
            currentDay = _getTotalDay();
            _logger.WriteLine($"[GetCurrentLevelCap] Current day from _getTotalDay: 0x{currentDay:X}");
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[GetCurrentLevelCap] Exception calling _getTotalDay: {ex.Message}");
            return 99;
        }

        int maxLevel = 1;
        foreach (var kvp in _levelCaps)
        {
            if (currentDay >= kvp.Key)
            {
                maxLevel = kvp.Value;
            }
            else
            {
                break;
            }
        }

        _logger.WriteLine($"[GetCurrentLevelCap] Returning max level cap: {maxLevel}");
        return maxLevel;
    }

    private bool IsPersonaAtLevelCap(IntPtr persona)
    {
        if (persona == IntPtr.Zero)
        {
            _logger.WriteLine("[IsPersonaAtLevelCap] persona pointer is zero");
            return false;
        }

        if (_getPersonaLevel == null)
        {
            _logger.WriteLine("[IsPersonaAtLevelCap] _getPersonaLevel delegate is null");
            return false;
        }

        byte currentLevel = 0;
        try
        {
            currentLevel = _getPersonaLevel(persona);
            _logger.WriteLine($"[IsPersonaAtLevelCap] Persona current level: {currentLevel}");
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[IsPersonaAtLevelCap] Exception calling _getPersonaLevel: {ex.Message}");
            return false;
        }

        int levelCap = GetCurrentLevelCap();
        bool atCap = currentLevel >= levelCap;
        _logger.WriteLine($"[IsPersonaAtLevelCap] Level cap: {levelCap}, At cap: {atCap}");
        return atCap;
    }

    private bool IsPartyMemberAtLevelCap(IntPtr partyMemberInfo)
    {
        if (partyMemberInfo == IntPtr.Zero)
        {
            _logger.WriteLine("[IsPartyMemberAtLevelCap] partyMemberInfo pointer is zero");
            return false;
        }

        if (_getPartyMemberLevel == null)
        {
            _logger.WriteLine("[IsPartyMemberAtLevelCap] _getPartyMemberLevel delegate is null");
            return false;
        }

        byte currentLevel = 0;
        try
        {
            currentLevel = _getPartyMemberLevel(partyMemberInfo);
            _logger.WriteLine($"[IsPartyMemberAtLevelCap] Party member current level: {currentLevel}");
        }
        catch (Exception ex)
        {
            _logger.WriteLine($"[IsPartyMemberAtLevelCap] Exception calling _getPartyMemberLevel: {ex.Message}");
            return false;
        }

        int levelCap = GetCurrentLevelCap();
        bool atCap = currentLevel >= levelCap;
        _logger.WriteLine($"[IsPartyMemberAtLevelCap] Level cap: {levelCap}, At cap: {atCap}");
        return atCap;
    }

    private void SetupResultsExp(BattleResults* results, astruct_2* param_2)
    {
        fixed (short* party = &_available[0])
        {
            _numAvailable = GetAvailableParty(party);
        }
        _setupExpHook.OriginalFunction(results, param_2);

        // Setup Party Exp
        for (PartyMember member = PartyMember.Yukari; member <= PartyMember.Koromaru; member++)
        {
            _expGains.Remove(member);
            _levelUps.Remove(member);
            if (!IsInactive(member, results)) continue;

            var persona = GetPartyMemberPersona(member);
            var level = persona->Level;
            int levelCap = GetCurrentLevelCap();
            if (level >= 99 || level >= levelCap)
            {
                Utils.LogDebug($"{member} is above or at level cap ({level} >= {levelCap}), skipping EXP gain.");
                continue;
            }

            int gainedExp = (int)(CalculateGainedExp(level, param_2) * _configuration.PartyExpMultiplier);
            var currentExp = persona->Exp;
            var requiredExp = GetPersonaRequiredExp(persona, (ushort)(level + 1));
            _expGains[member] = gainedExp;
            if (requiredExp <= currentExp + gainedExp)
            {
                Utils.LogDebug($"{member} is ready to level up");
                results->LevelUpStatus |= 0x10; // signify that a party member is ready to level up
                var statChanges = new PersonaStatChanges { };
                GenerateLevelUpPersona(persona, &statChanges, gainedExp);
                _levelUps[member] = statChanges;
            }
        }

        // Setup Protag Persona Exp
        var activePersona = GetPartyMemberPersona(PartyMember.Protag);
        int levelCapForProtag = GetCurrentLevelCap();
        for (short i = 0; i < 12; i++)
        {
            var persona = GetProtagPersona(i);
            if (persona == (Persona*)0 || persona->Id == activePersona->Id)
                continue;

            var level = persona->Level;
            if (level >= 99 || level >= levelCapForProtag)
            {
                Utils.LogDebug($"Protag Persona {i} ({persona->Id}) is at or above level cap ({level} >= {levelCapForProtag}), skipping EXP.");
                results->ProtagExpGains[i] = 0;
                continue;
            }

            int gainedExp = (int)(CalculateGainedExp(level, param_2) * _configuration.PersonaExpMultiplier);
            results->ProtagExpGains[i] += (uint)gainedExp;

            Utils.LogDebug($"Giving Protag Persona {i} ({persona->Id}) {gainedExp} exp");

            var currentExp = persona->Exp;
            var requiredExp = GetPersonaRequiredExp(persona, (ushort)(level + 1));
            if (requiredExp <= currentExp + gainedExp)
            {
                Utils.LogDebug($"Protag Persona {i} ({persona->Id}) is ready to level up");
                results->LevelUpStatus |= 8; // signify that a protag persona is ready to level up
                GenerateLevelUpPersona(persona, &(&results->ProtagPersonaChanges)[i], gainedExp);
            }
        }
    }


    private bool IsInactive(PartyMember member, BattleResults* results)
    {
        // Check if they're already in the party
        for (int i = 0; i < 4; i++)
        {
            if (results->PartyMembers[i] == (short)member) return false;
        }

        // Check if they're available
        for(int i = 0; i < _numAvailable; i++)
        {
            if (_available[i] == (short)member) return true;
        }
        return false;
    }

    private void GivePartyMemberExp(BattleResults* results, nuint param_2, nuint param_3, nuint param_4)
    {
        _givePartyMemberExpHook.OriginalFunction(results, param_2, param_3, param_4);

        for (PartyMember member = PartyMember.Yukari; member <= PartyMember.Koromaru; member++)
        {
            if (!IsInactive(member, results) || !_expGains.ContainsKey(member)) continue;

            var persona = GetPartyMemberPersona(member);
            var expGained = _expGains[member];

            if (IsPartyMemberAtLevelCap(new IntPtr(persona)))
            {
                Utils.LogDebug($"{member} is at/above level cap, zeroing EXP gain.");
                expGained = 0;
            }
            else
            {
                persona->Exp += expGained;
                Utils.LogDebug($"Gave {expGained} exp to {member}");
            }
               
            if (CanPersonaLevelUp(persona, (nuint)expGained, param_3, param_4) != 0)
            {
                var statChanges = _levelUps[member];
                Utils.LogError($"Levelling up {member} without menu display, this shouldn't happen!");
                LevelUpPersona(persona, &statChanges);
            }
        }
    }

    private nuint LevelUpPartyMember(BattleResultsThing* resultsThing)
    {
        var thing = resultsThing->Thing;
        // Give exp to any inactive members that didn't level up
        if(_levelUps.Count == 0 && _expGains.Count > 0)
        {
            for(int i = 0; i < _expGains.Count; i++)
            {
                var partyMember = _expGains.First().Key;
                var persona = GetPartyMemberPersona(partyMember);
                var expGained = _expGains[partyMember];
                persona->Exp += expGained;
                _expGains.Remove(partyMember);
                Utils.LogDebug($"Gave {expGained} exp to {partyMember}");
            }
        }

        // We only want to change stuff in state 1 (when it's picking a Persona's level up stuff to deal with)
        if (thing->State > 1 || _levelUps.Count == 0)
        {
            return _levelUpPartyMemberHook.OriginalFunction(resultsThing);
        }

        var results = &thing->Info->Results;

        Utils.LogDebug($"LevelUpSlot = {thing->LevelUpSlot}");
        for(int i = 0; i < 4; i++)
        {
            Utils.LogDebug($"Slot {i} is {(PartyMember)results->PartyMembers[i]} who has {(&results->PersonaChanges)[i].LevelIncrease} level increases and {results->ExpGains[i]} exp gained.");
        }

        // Wait until all of the real level ups have been done so we can safely overwrite their data
        for (int i = thing->LevelUpSlot; i < 4; i++)
        {
            var curMember = results->PartyMembers[i];
            if (curMember == 0) continue;

            if ((&results->PersonaChanges)[i].LevelIncrease != 0)
            {
                return _levelUpPartyMemberHook.OriginalFunction(resultsThing);
            }
            else
            {
                // Give them the exp ourself and clear them to ensure they get it exactly once
                GetPartyMemberPersona((PartyMember)curMember)->Exp += (int)results->ExpGains[i];
                results->PartyMembers[i] = 0;
            }
        }

        // Clear all of the real level ups so they can't loop
        for(int i = 1; i < 4; i++)
            results->PartyMembers[i] = 0;

        // Change the data of an active party member to an inactive one
        thing->LevelUpSlot = 0;
        var levelUp = _levelUps.First();
        var member = levelUp.Key;
        var statChanges = levelUp.Value;
        results->PartyMembers[0] = (short)member;
        results->ExpGains[0] = (uint)_expGains[member];
        results->PersonaChanges = statChanges;

        Utils.LogDebug($"Leveling up {member}");
        _levelUps.Remove(member);
        _expGains.Remove(member);

        return _levelUpPartyMemberHook.OriginalFunction(resultsThing);

    }

    private delegate void SetupResultsExpDelegate(BattleResults* results, astruct_2* param_2);
    private delegate void GivePartyMemberExpDelegate(BattleResults* results, nuint param_2, nuint param_3, nuint param_4);
    private delegate nuint LevelUpPartyMemberDelegate(BattleResultsThing* results);

    #region Standard Overrides
    public override void ConfigurationUpdated(Config configuration)
    {
        // Apply settings from configuration.
        // ... your code here.
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
    }
    #endregion

    #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}