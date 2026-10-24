namespace ClassicaCodex.Ingestion.Defixiones;

/// <summary>
/// Which curse tablets to fetch, and from where. Each source publishes one
/// file per text and none publishes "the curse tablets" as a download, so the
/// list of texts is kept here - taken from each source's own classification
/// in October 2026 - and the files are fetched one at a time.
///
/// All three sources are closed or slow-moving: the Attic volume of the
/// Inscriptiones Graecae is a finished corpus, the Heidelberg database stopped
/// in 2021, and I.Sicily is pinned to a commit. A new tablet in any of them
/// will not appear here until this list is renewed, which is the price of
/// citations that stay where they are.
/// </summary>
public static class DefixionesCatalogue
{
    /// <summary>
    /// Inscriptiones Graecae II/III³ 8, 1: Defixiones Atticae, ed. Jaime
    /// Curbera (Berlin-Brandenburg Academy), CC BY 4.0. Every curse tablet
    /// found in Attica - 486 of them - with English and German translations.
    /// </summary>
    public static readonly IReadOnlyList<string> AtticNumbers = new[]
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
        "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
        "21", "22", "23", "24", "25", "26", "27", "28", "29", "30",
        "31", "32", "33", "34", "35", "36", "37", "38", "39", "40",
        "41", "42", "43", "44", "45", "46", "47", "48", "49", "50",
        "51", "52", "53", "54", "55", "56", "57", "58", "59", "60",
        "61", "62", "63", "64", "65", "66", "67", "68", "69", "70",
        "71", "72", "73", "74", "75", "76", "77", "78", "79", "80",
        "81", "82", "83", "84", "85", "86", "87", "88", "89", "90",
        "91", "92", "93", "94", "95", "96", "97", "98", "99", "100",
        "101", "102", "103", "104", "105", "106", "107", "108", "109", "110",
        "111", "112", "113", "114", "115", "116", "117", "118", "119", "120",
        "121", "122", "123", "124", "125", "126", "127", "128", "129", "130",
        "131", "132", "133", "134", "135", "136", "137", "138", "139", "140",
        "141", "142", "143", "144", "145", "146", "147", "148", "149", "150",
        "151", "152", "153", "154", "155", "156", "157", "158", "159", "160",
        "161", "162", "163", "164", "165", "166", "167", "168", "169", "170",
        "171", "172", "173", "174", "175 I", "175 II", "176", "177", "178", "179",
        "180", "181", "182", "183", "184", "185", "186", "187", "188", "189",
        "190", "191", "192", "193", "194", "195", "196", "197", "198", "199",
        "200", "201", "202", "203", "204", "205", "206", "207", "208", "209",
        "210", "211", "212", "213", "214", "215", "216", "217", "218", "219",
        "220", "221", "222", "223", "224", "225", "226", "227", "228", "229",
        "230", "231", "232", "233", "234", "235", "236", "237", "238", "239",
        "240", "241", "242", "243", "244", "245", "246", "247", "248", "249",
        "250", "251", "252", "253", "254", "255", "256", "257", "258", "259",
        "260", "261", "262", "263", "264", "265", "266", "267", "268", "269",
        "270", "271", "272", "273", "274", "275", "276", "277", "278", "279",
        "280", "281", "282", "283", "284", "285", "286", "287", "288", "289",
        "290", "291", "292", "293", "294", "295", "296", "297", "298", "299",
        "300", "301", "302", "303", "304", "305", "306", "307", "308", "309",
        "310", "311", "312", "313", "314", "315", "316", "317", "318", "319",
        "320", "321", "322", "323", "324", "325", "326", "327", "328", "329",
        "330", "331", "332", "333", "334", "335", "336", "337", "338", "339",
        "340", "341", "342", "343", "344", "345", "346", "347", "348", "349",
        "350", "351", "352", "353", "354", "355", "356", "357", "358", "359",
        "360", "361", "362", "363", "364", "365", "366", "367", "368", "369",
        "370", "371", "372", "373", "374", "375", "376", "377", "378", "379",
        "380", "381", "382", "383", "384", "385", "386", "387", "388", "389",
        "390", "391", "392", "393", "394", "395", "396", "397", "398", "399",
        "400", "401", "402", "403", "404", "405", "406", "407", "408", "409",
        "410", "411", "412", "413", "414", "415", "416", "417", "418", "419",
        "420", "421", "422", "423", "424", "425", "426", "427", "428", "429",
        "430", "431", "432", "433", "434", "435", "436", "437", "438", "439",
        "440", "441", "442", "443", "444", "445", "446", "447", "448", "449",
        "450", "451", "452", "453", "454", "455", "456", "457", "458", "459",
        "460", "461", "462", "463", "464", "465", "466", "467", "468", "469",
        "470", "471", "472", "473", "474", "475", "476", "477", "478", "479",
        "480", "481", "482", "483", "484", "485",
    };

    /// <summary>
    /// The API's address for a tablet. A tablet in two parts is "175 I" here
    /// and "175(I)" to the API, which answers the spaced form with a server
    /// error.
    /// </summary>
    public static string AtticUrl(string number) =>
        "https://telota.bbaw.de/ig/api/xml/" + Uri.EscapeDataString(
            $"IG II_III³ 8, 1, {System.Text.RegularExpressions.Regex.Replace(number, @"^(\d+) ([IVX]+)$", "$1($2)")}");

    /// <summary>
    /// I.Sicily (Jonathan Prag, Oxford), CC BY 4.0: the inscriptions of
    /// ancient Sicily classed as defixio - lead tablets from Selinous, Kamarina,
    /// Gela, Messina and elsewhere. 70 files; some are placeholders
    /// without a text yet, and are skipped when read.
    /// </summary>
    public static readonly IReadOnlyList<string> SicilyIds = new[]
    {
        "ISic030003", "ISic030004", "ISic030005", "ISic030006", "ISic030007", "ISic030008",
        "ISic030009", "ISic030010", "ISic030011", "ISic030013", "ISic030014", "ISic030015",
        "ISic030019", "ISic030020", "ISic030022", "ISic030025", "ISic030031", "ISic030032",
        "ISic030033", "ISic030034", "ISic030035", "ISic030036", "ISic030037", "ISic030067",
        "ISic030068", "ISic030069", "ISic030070", "ISic030071", "ISic030072", "ISic030073",
        "ISic030074", "ISic030075", "ISic030076", "ISic030077", "ISic030078", "ISic030079",
        "ISic030080", "ISic030081", "ISic030082", "ISic030083", "ISic030084", "ISic030085",
        "ISic030086", "ISic030087", "ISic030088", "ISic030089", "ISic030090", "ISic030091",
        "ISic030092", "ISic030093", "ISic030094", "ISic030095", "ISic030096", "ISic030097",
        "ISic030098", "ISic030099", "ISic030100", "ISic030101", "ISic030102", "ISic030103",
        "ISic030104", "ISic030105", "ISic030106", "ISic030107", "ISic030108", "ISic030109",
        "ISic030110", "ISic030111", "ISic030303", "ISic030330",
    };

    public const string SicilyCommit = "572d7f5ee1cbb62087bfed3c46ffc7cab6cb386c";

    public static string SicilyUrl(string id) =>
        $"https://raw.githubusercontent.com/ISicily/ISicily/{SicilyCommit}/inscriptions/{id}.xml";

    /// <summary>
    /// The Epigraphic Database Heidelberg, CC BY-SA 4.0: every inscription of
    /// the type defixio (311) - Latin curse tablets from Britain, the Rhine,
    /// Spain, Pannonia and Italy, and a few in Greek letters. The database
    /// ended in 2021; its open data repository is pinned to a commit.
    /// </summary>
    public static readonly IReadOnlyList<string> HeidelbergIds = new[]
    {
        "HD000009", "HD000012", "HD000066", "HD000090", "HD000093", "HD000096", "HD000399", "HD001024",
        "HD001027", "HD001030", "HD001033", "HD001036", "HD001039", "HD001042", "HD001045", "HD001048",
        "HD001051", "HD001057", "HD001464", "HD001467", "HD001470", "HD001473", "HD002084", "HD002104",
        "HD003718", "HD004106", "HD004550", "HD004630", "HD004633", "HD007042", "HD007045", "HD007063",
        "HD007728", "HD007734", "HD008086", "HD008089", "HD008092", "HD008098", "HD008942", "HD008951",
        "HD009689", "HD012422", "HD012425", "HD012428", "HD013525", "HD013528", "HD013531", "HD013929",
        "HD015615", "HD015618", "HD015967", "HD017875", "HD018150", "HD018602", "HD019061", "HD019064",
        "HD019067", "HD019330", "HD019906", "HD021808", "HD023576", "HD024366", "HD024943", "HD025425",
        "HD025800", "HD026431", "HD026620", "HD026623", "HD026626", "HD026750", "HD026753", "HD027805",
        "HD028136", "HD031608", "HD035450", "HD035451", "HD035452", "HD035453", "HD035454", "HD035565",
        "HD035568", "HD035604", "HD035854", "HD035855", "HD035907", "HD035910", "HD035956", "HD036000",
        "HD036154", "HD037021", "HD041986", "HD042054", "HD042070", "HD042079", "HD042080", "HD043975",
        "HD044354", "HD044355", "HD044356", "HD044686", "HD044687", "HD044691", "HD046011", "HD046146",
        "HD046225", "HD047554", "HD047662", "HD047991", "HD048370", "HD048373", "HD048858", "HD048868",
        "HD049547", "HD050136", "HD050368", "HD050483", "HD050484", "HD050817", "HD050818", "HD050819",
        "HD050820", "HD051474", "HD051495", "HD051573", "HD051574", "HD051931", "HD052039", "HD052048",
        "HD052121", "HD052165", "HD052179", "HD052180", "HD052181", "HD052736", "HD052810", "HD052824",
        "HD053119", "HD053281", "HD053285", "HD053302", "HD054658", "HD054660", "HD054661", "HD054662",
        "HD054663", "HD054664", "HD054665", "HD055268", "HD056666", "HD057898", "HD058312", "HD062120",
        "HD064187", "HD065173", "HD065541", "HD065542", "HD065543", "HD065757", "HD065758", "HD065759",
        "HD065760", "HD065761", "HD065762", "HD065763", "HD065764", "HD065866", "HD065909", "HD066874",
        "HD067460", "HD067821", "HD068748", "HD068792", "HD068794", "HD068823", "HD068824", "HD068876",
        "HD068889", "HD068965", "HD069587", "HD069604", "HD071459", "HD071623", "HD071624", "HD072436",
        "HD072497", "HD072709", "HD072710", "HD072711", "HD072712", "HD075172", "HD075173", "HD075235",
        "HD075975", "HD075976", "HD075977", "HD075978", "HD075979", "HD075980", "HD075981", "HD075983",
        "HD075984", "HD076049", "HD076343", "HD076467", "HD076468", "HD076472", "HD076474", "HD076476",
        "HD076517", "HD076518", "HD076519", "HD076521", "HD076730", "HD079419", "HD079420", "HD079421",
        "HD079425", "HD079426", "HD079427", "HD079428", "HD079429", "HD079430", "HD079431", "HD079432",
        "HD079433", "HD079434", "HD079435", "HD079436", "HD079437", "HD079438", "HD079439", "HD079440",
        "HD079441", "HD079442", "HD079443", "HD079444", "HD079445", "HD079446", "HD079447", "HD079448",
        "HD079449", "HD079450", "HD079451", "HD079454", "HD079455", "HD079456", "HD079457", "HD079458",
        "HD079459", "HD079460", "HD079461", "HD079462", "HD079463", "HD079464", "HD079465", "HD079466",
        "HD079467", "HD079468", "HD079469", "HD079470", "HD079471", "HD079472", "HD079473", "HD079474",
        "HD079475", "HD079476", "HD079477", "HD079478", "HD079479", "HD079480", "HD079481", "HD079482",
        "HD079508", "HD079511", "HD079513", "HD079515", "HD079517", "HD079518", "HD079519", "HD079525",
        "HD079534", "HD079537", "HD079538", "HD079539", "HD079540", "HD079541", "HD079542", "HD079543",
        "HD079544", "HD079545", "HD079546", "HD079547", "HD079548", "HD079549", "HD079550", "HD079551",
        "HD079552", "HD079553", "HD079554", "HD079555", "HD079556", "HD079557", "HD079558", "HD079559",
        "HD079560", "HD079561", "HD079977", "HD081138", "HD081272", "HD082815", "HD082820",
    };

    public const string HeidelbergCommit = "45f166654ab4551a1954617a0df8e56fa7724ccb";

    /// <summary>
    /// The repository files inscriptions by ten-thousands and then thousands,
    /// counting from 1: HD012345 is inscriptions/2/3/HD012345.xml, and HD036000
    /// is in 4/7 with the rest of 36000-36999.
    /// </summary>
    public static string HeidelbergUrl(string id)
    {
        var n = int.Parse(id[2..]);
        var folder = $"{n / 10000 + 1}/{n % 10000 / 1000 + 1}";
        return $"https://raw.githubusercontent.com/epigraphic-database-heidelberg/data/{HeidelbergCommit}/inscriptions/{folder}/{id}.xml";
    }

    /// <summary>
    /// The repository stops at HD079999; the database went on to HD082828
    /// before it was frozen. Those last inscriptions are only in the
    /// database's own EpiDoc dump, a 5 MB zip under the same licence, so the
    /// four curse tablets among them come from there.
    /// </summary>
    public const int HeidelbergRepositoryLast = 79999;

    public const string HeidelbergLateDumpUrl =
        "https://edh.ub.uni-heidelberg.de/data/download/edhEpidocDump_HD080001-HD082828.zip";
}
