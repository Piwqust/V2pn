using System.Text.RegularExpressions;

namespace v2rayN.Desktop.Common;

/// <summary>A point on the map with a name to show beside it, e.g. a server's city or the person's own.</summary>
public sealed record MapPlace(double Lat, double Lon, string? Name, string? Code);

/// <summary>
/// Cities that server names mention ("Frankfurt-01", "🇳🇱 Амстердам"), so the map pin stands in the city instead
/// of the middle of the country: a New York server is not in Kansas. Coordinates to a tenth of a degree.
/// </summary>
public static class MapCities
{
    private sealed record City(string Code, double Lat, double Lon, string En, string Ru, string[] Aliases);

    private static readonly City[] _cities =
    [
        new("US", 40.71, -74.01, "New York", "Нью-Йорк", ["new york", "nyc", "нью-йорк"]),
        new("US", 34.05, -118.24, "Los Angeles", "Лос-Анджелес", ["los angeles", "лос-анджелес"]),
        new("US", 41.88, -87.63, "Chicago", "Чикаго", ["chicago", "чикаго"]),
        new("US", 32.78, -96.80, "Dallas", "Даллас", ["dallas", "даллас"]),
        new("US", 25.76, -80.19, "Miami", "Майами", ["miami", "майами"]),
        new("US", 47.61, -122.33, "Seattle", "Сиэтл", ["seattle", "сиэтл"]),
        new("US", 37.34, -121.89, "San Jose", "Сан-Хосе", ["silicon valley", "san jose", "кремниевая долина", "сан-хосе"]),
        new("US", 39.04, -77.49, "Ashburn", "Ашберн", ["ashburn", "ашберн"]),
        new("CA", 43.65, -79.38, "Toronto", "Торонто", ["toronto", "торонто"]),
        new("CA", 45.50, -73.57, "Montreal", "Монреаль", ["montreal", "монреаль"]),
        new("GB", 51.51, -0.13, "London", "Лондон", ["london", "лондон"]),
        new("NL", 52.37, 4.90, "Amsterdam", "Амстердам", ["amsterdam", "амстердам"]),
        new("BE", 50.85, 4.35, "Brussels", "Брюссель", ["brussels", "брюссель"]),
        new("DE", 50.11, 8.68, "Frankfurt", "Франкфурт", ["frankfurt", "франкфурт"]),
        new("DE", 52.52, 13.40, "Berlin", "Берлин", ["berlin", "берлин"]),
        new("DE", 49.45, 11.08, "Nuremberg", "Нюрнберг", ["nuremberg", "nürnberg", "нюрнберг"]),
        new("DE", 50.48, 12.37, "Falkenstein", "Фалькенштайн", ["falkenstein", "фалькенштайн"]),
        new("FR", 48.86, 2.35, "Paris", "Париж", ["paris", "париж"]),
        new("ES", 40.42, -3.70, "Madrid", "Мадрид", ["madrid", "мадрид"]),
        new("PT", 38.72, -9.14, "Lisbon", "Лиссабон", ["lisbon", "лиссабон"]),
        new("IT", 45.46, 9.19, "Milan", "Милан", ["milan", "милан"]),
        new("IT", 41.90, 12.50, "Rome", "Рим", ["rome", "рим"]),
        new("CH", 47.37, 8.54, "Zurich", "Цюрих", ["zurich", "zürich", "цюрих"]),
        new("AT", 48.21, 16.37, "Vienna", "Вена", ["vienna", "wien", "вена"]),
        new("CZ", 50.08, 14.44, "Prague", "Прага", ["prague", "praha", "прага"]),
        new("PL", 52.23, 21.01, "Warsaw", "Варшава", ["warsaw", "warszawa", "варшава"]),
        new("HU", 47.50, 19.04, "Budapest", "Будапешт", ["budapest", "будапешт"]),
        new("RO", 44.43, 26.10, "Bucharest", "Бухарест", ["bucharest", "бухарест"]),
        new("BG", 42.70, 23.32, "Sofia", "София", ["sofia", "софия"]),
        new("RS", 44.79, 20.45, "Belgrade", "Белград", ["belgrade", "белград"]),
        new("IE", 53.35, -6.26, "Dublin", "Дублин", ["dublin", "дублин"]),
        new("DK", 55.68, 12.57, "Copenhagen", "Копенгаген", ["copenhagen", "копенгаген"]),
        new("NO", 59.91, 10.75, "Oslo", "Осло", ["oslo", "осло"]),
        new("SE", 59.33, 18.07, "Stockholm", "Стокгольм", ["stockholm", "стокгольм"]),
        new("FI", 60.17, 24.94, "Helsinki", "Хельсинки", ["helsinki", "хельсинки"]),
        new("EE", 59.44, 24.75, "Tallinn", "Таллин", ["tallinn", "таллин", "таллинн"]),
        new("LV", 56.95, 24.11, "Riga", "Рига", ["riga", "рига"]),
        new("LT", 54.69, 25.28, "Vilnius", "Вильнюс", ["vilnius", "вильнюс"]),
        new("BY", 53.90, 27.56, "Minsk", "Минск", ["minsk", "минск"]),
        new("UA", 50.45, 30.52, "Kyiv", "Киев", ["kyiv", "kiev", "киев"]),
        new("RU", 55.76, 37.62, "Moscow", "Москва", ["moscow", "москва"]),
        new("RU", 59.94, 30.31, "Saint Petersburg", "Санкт-Петербург", ["spb", "saint petersburg", "st petersburg", "петербург", "санкт-петербург"]),
        new("TR", 41.01, 28.98, "Istanbul", "Стамбул", ["istanbul", "стамбул"]),
        new("GE", 41.72, 44.79, "Tbilisi", "Тбилиси", ["tbilisi", "тбилиси"]),
        new("AM", 40.18, 44.51, "Yerevan", "Ереван", ["yerevan", "ереван"]),
        new("KZ", 43.24, 76.89, "Almaty", "Алматы", ["almaty", "алматы"]),
        new("KZ", 51.17, 71.45, "Astana", "Астана", ["astana", "астана"]),
        new("UZ", 41.30, 69.24, "Tashkent", "Ташкент", ["tashkent", "ташкент"]),
        new("KG", 42.87, 74.59, "Bishkek", "Бишкек", ["bishkek", "бишкек"]),
        new("AE", 25.20, 55.27, "Dubai", "Дубай", ["dubai", "дубай"]),
        new("IN", 19.08, 72.88, "Mumbai", "Мумбаи", ["mumbai", "мумбаи"]),
        new("SG", 1.35, 103.82, "Singapore", "Сингапур", ["singapore", "сингапур"]),
        new("HK", 22.32, 114.17, "Hong Kong", "Гонконг", ["hong kong", "гонконг"]),
        new("JP", 35.68, 139.69, "Tokyo", "Токио", ["tokyo", "токио"]),
        new("JP", 34.69, 135.50, "Osaka", "Осака", ["osaka", "осака"]),
        new("KR", 37.57, 126.98, "Seoul", "Сеул", ["seoul", "сеул"]),
        new("AU", -33.87, 151.21, "Sydney", "Сидней", ["sydney", "сидней"]),
        new("BR", -23.55, -46.63, "São Paulo", "Сан-Паулу", ["sao paulo", "são paulo", "сан-паулу"]),
    ];

    // Where a country's dot stands when the middle of its largest landmass says little about where its servers are.
    private static readonly Dictionary<string, (double Lat, double Lon)> _representative = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RU"] = (55.76, 37.62),
        ["CA"] = (43.65, -79.38),
        ["AU"] = (-33.87, 151.21),
        ["BR"] = (-23.55, -46.63),
        ["ID"] = (-6.21, 106.85),
    };

    private static readonly Lazy<(Regex Regex, Dictionary<string, City> Lookup)> _index = new(() =>
    {
        var lookup = new Dictionary<string, City>(StringComparer.Ordinal);
        foreach (var city in _cities)
        {
            foreach (var alias in city.Aliases)
            {
                lookup[alias] = city;
            }
        }
        var pattern = string.Join("|", lookup.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        var regex = new Regex($@"(?<![\p{{L}}])(?:{pattern})(?![\p{{L}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        return (regex, lookup);
    });

    /// <summary>The city a server's name mentions, when it lies in the country the server was placed in.</summary>
    public static MapPlace? Find(string? remarks, string? code)
    {
        if (remarks.IsNullOrEmpty() || code.IsNullOrEmpty())
        {
            return null;
        }
        var (regex, lookup) = _index.Value;
        foreach (Match match in regex.Matches(remarks))
        {
            if (lookup.TryGetValue(match.Value.ToLowerInvariant(), out var city)
                && string.Equals(city.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return new MapPlace(city.Lat, city.Lon, Name(city), city.Code);
            }
        }
        return null;
    }

    /// <summary>A point for the country as a whole where its geometric middle would mislead, e.g. Russia's in Siberia.</summary>
    public static (double Lat, double Lon)? Representative(string? code) =>
        code != null && _representative.TryGetValue(code, out var point) ? point : null;

    private static string Name(City city) =>
        CultureInfo.CurrentUICulture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? city.Ru : city.En;
}
