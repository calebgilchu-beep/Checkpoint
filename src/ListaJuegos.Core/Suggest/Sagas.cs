using ListaJuegos.Core.Connectors;

namespace ListaJuegos.Core.Suggest;

/// <summary>Diccionario de sagas conocidas para proponer la categoría de un juego.
/// Cada línea: «Nombre de la saga|patrón|patrón…». Un patrón con «^» debe ir al principio del título;
/// sin él, basta con que aparezca como palabras completas.</summary>
public static class Sagas
{
    private static readonly string[] Data =
    {
        "Resident Evil|^resident evil|^biohazard", "Metal Gear Solid|^metal gear", "Silent Hill|^silent hill", "Dead Space|^dead space",
        "Super Mario Bros|^super mario|^new super mario|^mario bros", "Mario Kart|^mario kart", "Mario Party|^mario party",
        "Paper Mario|^paper mario", "Mario & Luigi|^mario and luigi|^mario luigi", "Yoshi|^yoshi", "Donkey Kong|^donkey kong",
        "Kirby|^kirby", "The Legend of Zelda|^the legend of zelda|^zelda", "Metroid|^metroid", "Pokémon|^pokemon",
        "Fire Emblem|^fire emblem", "Xenoblade Chronicles|^xenoblade", "Splatoon|^splatoon", "Animal Crossing|^animal crossing",
        "Super Smash Bros|^super smash", "Pikmin|^pikmin", "Luigi's Mansion|^luigi s mansion|^luigis mansion", "Star Fox|^star fox",
        "Persona|^persona|^shin megami tensei persona", "Shin Megami Tensei|^shin megami tensei", "Final Fantasy|^final fantasy",
        "Dragon Quest|^dragon quest", "Kingdom Hearts|^kingdom hearts", "Tales of|^tales of", "Yakuza / Like a Dragon|^yakuza|^like a dragon",
        "Monster Hunter|^monster hunter", "Devil May Cry|^devil may cry|^dmc", "Street Fighter|^street fighter", "Tekken|^tekken",
        "Mortal Kombat|^mortal kombat", "Dark Souls|^dark souls", "Elden Ring|^elden ring", "Sekiro|^sekiro",
        "Armored Core|^armored core", "Demon's Souls|^demon s souls|^demons souls", "Nier|^nier", "Danganronpa|^danganronpa",
        "Ace Attorney|ace attorney", "Professor Layton|^professor layton|^el profesor layton", "Ys|^ys ", "Trails|^the legend of heroes|^trails",
        "Atelier|^atelier", "Ni no Kuni|^ni no kuni", "Octopath Traveler|^octopath", "Bravely Default|^bravely", "Dragon Ball|^dragon ball",
        "Naruto|^naruto", "One Piece|^one piece", "Sonic|^sonic", "Crash Bandicoot|^crash", "Spyro|^spyro", "Rayman|^rayman",
        "Ratchet & Clank|^ratchet", "Jak and Daxter|^jak", "Sly Cooper|^sly", "God of War|^god of war", "Uncharted|^uncharted",
        "The Last of Us|^the last of us", "Horizon|^horizon zero dawn|^horizon forbidden west", "Ghost of Tsushima|^ghost of",
        "Gran Turismo|^gran turismo", "Spider-Man|spider man", "Batman Arkham|^batman arkham", "Halo|^halo", "Gears of War|^gears",
        "Forza|^forza", "Fable|^fable", "Age of Empires|^age of empires", "Microsoft Flight Simulator|^microsoft flight simulator",
        "Doom|^doom", "Quake|^quake", "Wolfenstein|^wolfenstein", "Fallout|^fallout", "The Elder Scrolls|^the elder scrolls|^skyrim|^oblivion|^morrowind",
        "Half-Life|^half life", "Portal|^portal", "Left 4 Dead|^left 4 dead", "Counter-Strike|^counter strike",
        "Team Fortress|^team fortress", "Call of Duty|^call of duty", "Battlefield|^battlefield", "Medal of Honor|^medal of honor",
        "Far Cry|^far cry", "Assassin's Creed|^assassin s creed|^assassins creed", "Prince of Persia|^prince of persia",
        "Tom Clancy's|^tom clancy s|^rainbow six|^ghost recon|^splinter cell|^the division", "Watch Dogs|^watch dogs",
        "Grand Theft Auto|^grand theft auto|^gta", "Red Dead|^red dead", "Max Payne|^max payne", "BioShock|^bioshock",
        "Borderlands|^borderlands", "Mass Effect|^mass effect", "Dragon Age|^dragon age", "Star Wars|^star wars", "The Witcher|^the witcher|^witcher",
        "Hitman|^hitman", "Tomb Raider|tomb raider", "Deus Ex|^deus ex", "Dishonored|^dishonored",
        "Mafia|^mafia", "Saints Row|^saints row", "Just Cause|^just cause", "Need for Speed|^need for speed",
        "Burnout|^burnout", "FIFA / EA Sports FC|^fifa|^ea sports fc", "Pro Evolution Soccer|^pro evolution soccer|^pes|^efootball",
        "NBA 2K|^nba 2k", "Tony Hawk's|^tony hawk", "The Sims|^the sims|^los sims", "SimCity|^simcity", "Civilization|civilization",
        "XCOM|^xcom", "Total War|total war", "Crusader Kings|^crusader kings", "Europa Universalis|^europa universalis",
        "StarCraft|^starcraft", "Warcraft|^warcraft|^world of warcraft", "Diablo|^diablo", "Overwatch|^overwatch",
        "Minecraft|^minecraft", "Hollow Knight|^hollow knight", "Ori|^ori and",
        "Undertale / Deltarune|^undertale|^deltarune", "Hades|^hades", "Dead Cells|^dead cells",
        "The Binding of Isaac|^the binding of isaac", "Shovel Knight|^shovel knight", "Castlevania|^castlevania", "Mega Man|^mega man|^megaman",
        "Contra|^contra", "Bomberman|^bomberman", "Pac-Man|^pac man", "Tetris|^tetris", "Katamari|katamari", "Ace Combat|^ace combat",
        "Dead Rising|^dead rising", "Dying Light|^dying light", "Dead Island|^dead island", "Outlast|^outlast", "Amnesia|^amnesia",
        "Five Nights at Freddy's|^five nights at freddy", "Alan Wake|^alan wake", "Life is Strange|^life is strange",
        "The Walking Dead|walking dead", "Death Stranding|^death stranding", "Kingdom Come|^kingdom come", "Baldur's Gate|^baldur s gate",
        "Divinity|^divinity", "Pillars of Eternity|^pillars of eternity", "Wasteland|^wasteland", "Metro|^metro 2033|^metro last light|^metro exodus",
        "S.T.A.L.K.E.R.|^s t a l k e r|^stalker", "Titanfall|^titanfall", "Destiny|^destiny",
        "Splinter Cell|^splinter cell", "Payday|^payday", "Sniper Elite|^sniper elite",
        "Lego|^lego", "Just Dance|^just dance", "Guitar Hero|^guitar hero", "Rock Band|^rock band", "Hogwarts Legacy|^hogwarts",
        "Harry Potter|^harry potter", "Middle-earth|^middle earth|^the lord of the rings|^el senor de los anillos", "Sackboy / LittleBigPlanet|^littlebigplanet|^sackboy",
        "Kingdom Rush|^kingdom rush", "Plants vs. Zombies|^plants vs zombies", "Angry Birds|^angry birds", "Worms|^worms",
        "Danganronpa|^danganronpa", "Zero Escape|^zero escape|^999", "Phoenix Wright|^phoenix wright", "Ys|^ys viii|^ys ix|^ys x",
        "Superhot|^superhot", "It Takes Two / Split Fiction|^it takes two|^split fiction|^a way out"
    };

    private static readonly List<(string name, List<(string pat, bool start)> pats)> Index =
        Data.Select(l => l.Split('|'))
            .Select(p => (p[0], p.Skip(1).Select(x => x.StartsWith('^') ? (TitleMatch.Key(x[1..]), true) : (TitleMatch.Key(x), false)).ToList()))
            .ToList();

    public static IReadOnlyList<string> All => Index.Select(i => i.name).Distinct().OrderBy(n => n, StringComparer.CurrentCulture).ToList();

    public static string? Find(string title)
    {
        var k = TitleMatch.Key(title);
        if (k.Length == 0) return null;
        foreach (var (name, pats) in Index)
            foreach (var (pat, start) in pats)
            {
                if (pat.Length == 0) continue;
                if (start ? (k == pat || k.StartsWith(pat + " ") || (pat.EndsWith(' ') && k.StartsWith(pat)))
                          : (" " + k + " ").Contains(" " + pat + " "))
                    return name;
            }
        return null;
    }
}
