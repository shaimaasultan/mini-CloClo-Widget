using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniCloClo;

// The places prayer times can be worked out for without going online: each
// with its coordinates (two decimals, about 1 km), its Windows time zone and
// the calculation method most used there. Nothing is looked up and the PC's
// own location is never read; another place can be typed in as coordinates.
public sealed record OfflineCity(string Name, string ArabicName, string Country, double Latitude, double Longitude,
    string TimeZoneId, string Method)
{
    public string Label => ArabicName.Length > 0 ? $"{Name} — {ArabicName} ({Country})" : $"{Name} ({Country})";
    public override string ToString() => Label;
}

public static class OfflineCities
{
    private const string Egypt = "Egypt Standard Time", Arab = "Arab Standard Time", Arabian = "Arabian Standard Time",
        Pakistan = "Pakistan Standard Time", India = "India Standard Time", Eastern = "Eastern Standard Time",
        Central = "Central Standard Time", Pacific = "Pacific Standard Time", WEurope = "W. Europe Standard Time",
        Romance = "Romance Standard Time", WCAfrica = "W. Central Africa Standard Time", EAfrica = "E. Africa Standard Time",
        Gmt = "GMT Standard Time", Greenwich = "Greenwich Standard Time", Morocco = "Morocco Standard Time",
        AusEastern = "AUS Eastern Standard Time", Singapore = "Singapore Standard Time";

    public static readonly IReadOnlyList<OfflineCity> All = new List<OfflineCity>
    {
        // Egypt
        new("Cairo", "القاهرة", "Egypt", 30.04, 31.24, Egypt, "egyptian"),
        new("Giza", "الجيزة", "Egypt", 30.01, 31.21, Egypt, "egyptian"),
        new("Alexandria", "الإسكندرية", "Egypt", 31.20, 29.92, Egypt, "egyptian"),
        new("Port Said", "بورسعيد", "Egypt", 31.26, 32.30, Egypt, "egyptian"),
        new("Suez", "السويس", "Egypt", 29.97, 32.53, Egypt, "egyptian"),
        new("Ismailia", "الإسماعيلية", "Egypt", 30.60, 32.27, Egypt, "egyptian"),
        new("Mansoura", "المنصورة", "Egypt", 31.04, 31.38, Egypt, "egyptian"),
        new("Tanta", "طنطا", "Egypt", 30.79, 31.00, Egypt, "egyptian"),
        new("Zagazig", "الزقازيق", "Egypt", 30.59, 31.50, Egypt, "egyptian"),
        new("Damanhur", "دمنهور", "Egypt", 31.03, 30.47, Egypt, "egyptian"),
        new("Faiyum", "الفيوم", "Egypt", 29.31, 30.84, Egypt, "egyptian"),
        new("Minya", "المنيا", "Egypt", 28.11, 30.75, Egypt, "egyptian"),
        new("Asyut", "أسيوط", "Egypt", 27.18, 31.18, Egypt, "egyptian"),
        new("Sohag", "سوهاج", "Egypt", 26.56, 31.69, Egypt, "egyptian"),
        new("Luxor", "الأقصر", "Egypt", 25.69, 32.64, Egypt, "egyptian"),
        new("Aswan", "أسوان", "Egypt", 24.09, 32.90, Egypt, "egyptian"),
        new("Hurghada", "الغردقة", "Egypt", 27.26, 33.81, Egypt, "egyptian"),
        new("Sharm El Sheikh", "شرم الشيخ", "Egypt", 27.92, 34.33, Egypt, "egyptian"),
        // Arabian Peninsula
        new("Makkah", "مكة المكرمة", "Saudi Arabia", 21.42, 39.83, Arab, "makkah"),
        new("Madinah", "المدينة المنورة", "Saudi Arabia", 24.47, 39.61, Arab, "makkah"),
        new("Riyadh", "الرياض", "Saudi Arabia", 24.71, 46.68, Arab, "makkah"),
        new("Jeddah", "جدة", "Saudi Arabia", 21.49, 39.19, Arab, "makkah"),
        new("Dammam", "الدمام", "Saudi Arabia", 26.43, 50.10, Arab, "makkah"),
        new("Dubai", "دبي", "UAE", 25.20, 55.27, Arabian, "makkah"),
        new("Abu Dhabi", "أبوظبي", "UAE", 24.45, 54.38, Arabian, "makkah"),
        new("Sharjah", "الشارقة", "UAE", 25.35, 55.39, Arabian, "makkah"),
        new("Doha", "الدوحة", "Qatar", 25.29, 51.53, Arab, "makkah"),
        new("Kuwait City", "الكويت", "Kuwait", 29.38, 47.99, Arab, "makkah"),
        new("Manama", "المنامة", "Bahrain", 26.23, 50.59, Arab, "makkah"),
        new("Muscat", "مسقط", "Oman", 23.59, 58.41, Arabian, "makkah"),
        new("Sana'a", "صنعاء", "Yemen", 15.37, 44.19, Arab, "mwl"),
        new("Aden", "عدن", "Yemen", 12.79, 45.03, Arab, "mwl"),
        // Levant, Iraq, Turkey, Iran
        new("Amman", "عمّان", "Jordan", 31.95, 35.93, "Jordan Standard Time", "mwl"),
        new("Jerusalem", "القدس", "Palestine", 31.77, 35.21, "Israel Standard Time", "mwl"),
        new("Gaza", "غزة", "Palestine", 31.50, 34.47, "West Bank Standard Time", "mwl"),
        new("Beirut", "بيروت", "Lebanon", 33.89, 35.50, "Middle East Standard Time", "mwl"),
        new("Damascus", "دمشق", "Syria", 33.51, 36.28, "Syria Standard Time", "mwl"),
        new("Aleppo", "حلب", "Syria", 36.20, 37.13, "Syria Standard Time", "mwl"),
        new("Baghdad", "بغداد", "Iraq", 33.32, 44.36, "Arabic Standard Time", "mwl"),
        new("Basra", "البصرة", "Iraq", 30.51, 47.78, "Arabic Standard Time", "mwl"),
        new("Istanbul", "إسطنبول", "Türkiye", 41.01, 28.98, "Turkey Standard Time", "mwl"),
        new("Ankara", "أنقرة", "Türkiye", 39.93, 32.86, "Turkey Standard Time", "mwl"),
        new("Tehran", "طهران", "Iran", 35.69, 51.39, "Iran Standard Time", "mwl"),
        // North and East Africa
        new("Khartoum", "الخرطوم", "Sudan", 15.50, 32.56, "Sudan Standard Time", "egyptian"),
        new("Tripoli", "طرابلس", "Libya", 32.89, 13.19, "Libya Standard Time", "egyptian"),
        new("Benghazi", "بنغازي", "Libya", 32.12, 20.09, "Libya Standard Time", "egyptian"),
        new("Tunis", "تونس", "Tunisia", 36.81, 10.18, WCAfrica, "mwl"),
        new("Algiers", "الجزائر", "Algeria", 36.75, 3.06, WCAfrica, "mwl"),
        new("Casablanca", "الدار البيضاء", "Morocco", 33.57, -7.59, Morocco, "mwl"),
        new("Rabat", "الرباط", "Morocco", 34.02, -6.84, Morocco, "mwl"),
        new("Nouakchott", "نواكشوط", "Mauritania", 18.07, -15.96, Greenwich, "mwl"),
        new("Mogadishu", "مقديشو", "Somalia", 2.05, 45.32, EAfrica, "mwl"),
        new("Djibouti", "جيبوتي", "Djibouti", 11.59, 43.15, EAfrica, "mwl"),
        new("Nairobi", "", "Kenya", -1.29, 36.82, EAfrica, "mwl"),
        new("Lagos", "", "Nigeria", 6.52, 3.38, WCAfrica, "mwl"),
        new("Kano", "", "Nigeria", 12.00, 8.52, WCAfrica, "mwl"),
        new("Dakar", "", "Senegal", 14.72, -17.47, Greenwich, "mwl"),
        // Asia
        new("Karachi", "كراتشي", "Pakistan", 24.86, 67.01, Pakistan, "karachi"),
        new("Lahore", "لاهور", "Pakistan", 31.55, 74.34, Pakistan, "karachi"),
        new("Islamabad", "إسلام آباد", "Pakistan", 33.68, 73.05, Pakistan, "karachi"),
        new("Kabul", "كابل", "Afghanistan", 34.56, 69.21, "Afghanistan Standard Time", "karachi"),
        new("Delhi", "دلهي", "India", 28.61, 77.21, India, "karachi"),
        new("Mumbai", "مومباي", "India", 19.08, 72.88, India, "karachi"),
        new("Hyderabad", "حيدر آباد", "India", 17.39, 78.49, India, "karachi"),
        new("Dhaka", "دكا", "Bangladesh", 23.81, 90.41, "Bangladesh Standard Time", "karachi"),
        new("Kuala Lumpur", "كوالالمبور", "Malaysia", 3.14, 101.69, Singapore, "mwl"),
        new("Singapore", "سنغافورة", "Singapore", 1.35, 103.82, Singapore, "mwl"),
        new("Jakarta", "جاكرتا", "Indonesia", -6.21, 106.85, "SE Asia Standard Time", "mwl"),
        new("Baku", "باكو", "Azerbaijan", 40.41, 49.87, "Azerbaijan Standard Time", "mwl"),
        new("Tashkent", "طشقند", "Uzbekistan", 41.30, 69.24, "West Asia Standard Time", "mwl"),
        // Europe
        new("London", "لندن", "United Kingdom", 51.51, -0.13, Gmt, "mwl"),
        new("Birmingham", "برمنغهام", "United Kingdom", 52.49, -1.89, Gmt, "mwl"),
        new("Manchester", "مانشستر", "United Kingdom", 53.48, -2.24, Gmt, "mwl"),
        new("Paris", "باريس", "France", 48.86, 2.35, Romance, "mwl"),
        new("Brussels", "بروكسل", "Belgium", 50.85, 4.35, Romance, "mwl"),
        new("Madrid", "مدريد", "Spain", 40.42, -3.70, Romance, "mwl"),
        new("Amsterdam", "أمستردام", "Netherlands", 52.37, 4.90, WEurope, "mwl"),
        new("Berlin", "برلين", "Germany", 52.52, 13.40, WEurope, "mwl"),
        new("Rome", "روما", "Italy", 41.90, 12.50, WEurope, "mwl"),
        new("Vienna", "فيينا", "Austria", 48.21, 16.37, WEurope, "mwl"),
        new("Stockholm", "ستوكهولم", "Sweden", 59.33, 18.07, WEurope, "mwl"),
        new("Moscow", "موسكو", "Russia", 55.76, 37.62, "Russian Standard Time", "mwl"),
        // Americas and Oceania
        new("New York", "نيويورك", "United States", 40.71, -74.01, Eastern, "isna"),
        new("Dearborn", "ديربورن", "United States", 42.32, -83.18, Eastern, "isna"),
        new("Chicago", "شيكاغو", "United States", 41.88, -87.63, Central, "isna"),
        new("Houston", "هيوستن", "United States", 29.76, -95.37, Central, "isna"),
        new("Los Angeles", "لوس أنجلوس", "United States", 34.05, -118.24, Pacific, "isna"),
        new("Toronto", "تورنتو", "Canada", 43.65, -79.38, Eastern, "isna"),
        new("Mississauga", "ميسيساجا", "Canada", 43.59, -79.64, Eastern, "isna"),
        new("Montreal", "مونتريال", "Canada", 45.50, -73.57, Eastern, "isna"),
        new("Sydney", "سيدني", "Australia", -33.87, 151.21, AusEastern, "mwl"),
        new("Melbourne", "ملبورن", "Australia", -37.81, 144.96, AusEastern, "mwl"),
    };

    public static OfflineCity? Find(string name) =>
        All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
