// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.WebAnalytics;

internal sealed record PrivacyText(string Title, string Intro, string NoStorage, string Details, string OptOut, string Confirmed);
internal sealed record PrivacyLabels(
    string Controller,
    string Contact,
    string Purpose,
    string LegalBasis,
    string Hosting,
    string DetailedRetention,
    string SessionRetention,
    string AnonymousAggregates,
    string NoticeVersion);

internal static class PrivacyTranslations
{
    private static readonly IReadOnlyDictionary<string, PrivacyText> Texts =
        new Dictionary<string, PrivacyText>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("Audience measurement", "We measure visits to improve this service.", "No analytical cookie or browser storage is used.", "The measurements are pseudonymous and limited to this application.", "Refuse audience measurement", "Your refusal has been saved."),
            ["fr"] = new("Mesure d’audience", "Nous mesurons les visites pour améliorer ce service.", "Aucun cookie analytique ni stockage navigateur n’est utilisé.", "Les mesures sont pseudonymes et limitées à cette application.", "Refuser la mesure d’audience", "Votre refus a été enregistré."),
            ["nl"] = new("Publieksmeting", "We meten bezoeken om deze dienst te verbeteren.", "Er worden geen analytische cookies of browseropslag gebruikt.", "De metingen zijn pseudoniem en beperkt tot deze toepassing.", "Publieksmeting weigeren", "Uw weigering is opgeslagen."),
            ["de"] = new("Reichweitenmessung", "Wir messen Besuche, um diesen Dienst zu verbessern.", "Es werden keine Analyse-Cookies oder Browser-Speicher verwendet.", "Die Messungen sind pseudonym und auf diese Anwendung beschränkt.", "Reichweitenmessung ablehnen", "Ihre Ablehnung wurde gespeichert."),
            ["es"] = new("Medición de audiencia", "Medimos las visitas para mejorar este servicio.", "No se utilizan cookies analíticas ni almacenamiento del navegador.", "Las mediciones son seudónimas y se limitan a esta aplicación.", "Rechazar la medición", "Su rechazo ha sido guardado."),
            ["it"] = new("Misurazione del pubblico", "Misuriamo le visite per migliorare questo servizio.", "Non vengono usati cookie analitici né memoria del browser.", "Le misurazioni sono pseudonime e limitate a questa applicazione.", "Rifiuta la misurazione", "Il rifiuto è stato salvato."),
            ["pt"] = new("Medição de audiência", "Medimos as visitas para melhorar este serviço.", "Não usamos cookies analíticos nem armazenamento do navegador.", "As medições são pseudónimas e limitadas a esta aplicação.", "Recusar a medição", "A sua recusa foi guardada."),
            ["pl"] = new("Pomiar odbiorców", "Mierzymy wizyty, aby ulepszać tę usługę.", "Nie używamy analitycznych plików cookie ani pamięci przeglądarki.", "Pomiary są pseudonimowe i ograniczone do tej aplikacji.", "Odmów pomiaru", "Twój sprzeciw został zapisany."),
            ["cs"] = new("Měření návštěvnosti", "Měříme návštěvy, abychom tuto službu zlepšili.", "Nepoužíváme analytické cookies ani úložiště prohlížeče.", "Měření jsou pseudonymní a omezená na tuto aplikaci.", "Odmítnout měření", "Vaše odmítnutí bylo uloženo."),
            ["sk"] = new("Meranie návštevnosti", "Meriame návštevy, aby sme túto službu zlepšili.", "Nepoužívame analytické cookies ani úložisko prehliadača.", "Merania sú pseudonymné a obmedzené na túto aplikáciu.", "Odmietnuť meranie", "Vaše odmietnutie bolo uložené."),
            ["sl"] = new("Merjenje obiska", "Obiske merimo za izboljšanje te storitve.", "Ne uporabljamo analitičnih piškotkov ali shrambe brskalnika.", "Meritve so psevdonimne in omejene na to aplikacijo.", "Zavrni merjenje", "Vaša zavrnitev je shranjena."),
            ["hr"] = new("Mjerenje posjećenosti", "Mjerimo posjete kako bismo poboljšali ovu uslugu.", "Ne koristimo analitičke kolačiće ni pohranu preglednika.", "Mjerenja su pseudonimna i ograničena na ovu aplikaciju.", "Odbij mjerenje", "Vaše odbijanje je spremljeno."),
            ["hu"] = new("Látogatottságmérés", "A szolgáltatás javításához mérjük a látogatásokat.", "Nem használunk analitikai sütit vagy böngészőtárhelyet.", "A mérések álnevesek és erre az alkalmazásra korlátozódnak.", "Mérés elutasítása", "Az elutasítást elmentettük."),
            ["ro"] = new("Măsurarea audienței", "Măsurăm vizitele pentru a îmbunătăți acest serviciu.", "Nu folosim cookie-uri analitice sau stocare în browser.", "Măsurătorile sunt pseudonime și limitate la această aplicație.", "Refuză măsurarea", "Refuzul dumneavoastră a fost salvat."),
            ["bg"] = new("Измерване на аудиторията", "Измерваме посещенията, за да подобрим услугата.", "Не използваме аналитични бисквитки или хранилище на браузъра.", "Измерванията са псевдонимни и ограничени до това приложение.", "Отказ от измерване", "Вашият отказ е запазен."),
            ["el"] = new("Μέτρηση κοινού", "Μετράμε τις επισκέψεις για να βελτιώσουμε την υπηρεσία.", "Δεν χρησιμοποιούνται αναλυτικά cookies ή αποθήκευση προγράμματος περιήγησης.", "Οι μετρήσεις είναι ψευδωνυμοποιημένες και περιορίζονται σε αυτή την εφαρμογή.", "Άρνηση μέτρησης", "Η άρνησή σας αποθηκεύτηκε."),
            ["da"] = new("Publikumsmåling", "Vi måler besøg for at forbedre denne tjeneste.", "Der bruges ingen analysecookies eller browserlager.", "Målingerne er pseudonyme og begrænset til denne applikation.", "Afvis måling", "Dit afslag er gemt."),
            ["sv"] = new("Publikmätning", "Vi mäter besök för att förbättra tjänsten.", "Inga analyscookies eller webbläsarlagring används.", "Mätningarna är pseudonyma och begränsade till denna applikation.", "Avböj mätning", "Ditt avböjande har sparats."),
            ["nb"] = new("Publikumsmåling", "Vi måler besøk for å forbedre denne tjenesten.", "Ingen analysecookie eller nettleserlagring brukes.", "Målingene er pseudonyme og begrenset til denne applikasjonen.", "Avslå måling", "Avslaget ditt er lagret."),
            ["fi"] = new("Yleisömittaus", "Mittaamme käyntejä palvelun parantamiseksi.", "Emme käytä analytiikkaevästeitä tai selaintallennusta.", "Mittaukset ovat pseudonyymejä ja rajattu tähän sovellukseen.", "Kieltäydy mittauksesta", "Kieltäytymisesi on tallennettu."),
            ["et"] = new("Külastatavuse mõõtmine", "Mõõdame külastusi teenuse parandamiseks.", "Me ei kasuta analüütikaküpsiseid ega brauseri salvestusruumi.", "Mõõtmised on pseudonüümsed ja piiratud selle rakendusega.", "Keeldu mõõtmisest", "Teie keeldumine on salvestatud."),
            ["lv"] = new("Apmeklējuma mērīšana", "Mēs mērām apmeklējumus, lai uzlabotu pakalpojumu.", "Netiek izmantotas analītikas sīkdatnes vai pārlūka krātuve.", "Mērījumi ir pseidonimizēti un attiecas tikai uz šo lietotni.", "Atteikties no mērīšanas", "Jūsu atteikums ir saglabāts."),
            ["lt"] = new("Lankomumo matavimas", "Matuojame apsilankymus, kad pagerintume paslaugą.", "Nenaudojami analitiniai slapukai ar naršyklės saugykla.", "Matavimai yra pseudoniminiai ir apriboti šia programa.", "Atsisakyti matavimo", "Jūsų atsisakymas išsaugotas."),
            ["ga"] = new("Tomhas lucht féachana", "Tomhaisimid cuairteanna chun an tseirbhís a fheabhsú.", "Ní úsáidtear fianáin anailíse ná stóras brabhsálaí.", "Tá na tomhais bréagainmnithe agus teoranta don fheidhmchlár seo.", "Diúltaigh don tomhas", "Sábháladh do dhiúltú.")
        };

    private static readonly IReadOnlyDictionary<string, PrivacyLabels> Labels =
        new Dictionary<string, PrivacyLabels>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("Controller", "Contact", "Purpose", "Legal basis", "Hosting", "Detailed retention", "Session retention", "Anonymous aggregates", "Notice version"),
            ["fr"] = new("Responsable", "Contact", "Finalité", "Base légale", "Hébergement", "Conservation détaillée", "Conservation des sessions", "Agrégats anonymes", "Version de l’information"),
            ["nl"] = new("Verwerkingsverantwoordelijke", "Contact", "Doel", "Rechtsgrond", "Hosting", "Bewaartermijn detailgegevens", "Bewaartermijn sessies", "Anonieme aggregaten", "Versie van de informatie"),
            ["de"] = new("Verantwortlicher", "Kontakt", "Zweck", "Rechtsgrundlage", "Hosting", "Aufbewahrung der Detaildaten", "Aufbewahrung der Sitzungen", "Anonyme Aggregate", "Version des Hinweises"),
            ["es"] = new("Responsable", "Contacto", "Finalidad", "Base jurídica", "Alojamiento", "Conservación detallada", "Conservación de sesiones", "Agregados anónimos", "Versión del aviso"),
            ["it"] = new("Titolare", "Contatto", "Finalità", "Base giuridica", "Hosting", "Conservazione dei dettagli", "Conservazione delle sessioni", "Aggregati anonimi", "Versione dell’informativa"),
            ["pt"] = new("Responsável", "Contacto", "Finalidade", "Base jurídica", "Alojamento", "Conservação detalhada", "Conservação das sessões", "Agregados anónimos", "Versão da informação"),
            ["pl"] = new("Administrator", "Kontakt", "Cel", "Podstawa prawna", "Hosting", "Przechowywanie danych szczegółowych", "Przechowywanie sesji", "Anonimowe agregaty", "Wersja informacji"),
            ["cs"] = new("Správce", "Kontakt", "Účel", "Právní základ", "Hosting", "Uchování podrobných údajů", "Uchování relací", "Anonymní souhrny", "Verze informace"),
            ["sk"] = new("Prevádzkovateľ", "Kontakt", "Účel", "Právny základ", "Hosting", "Uchovávanie podrobných údajov", "Uchovávanie relácií", "Anonymné súhrny", "Verzia informácie"),
            ["sl"] = new("Upravljavec", "Kontakt", "Namen", "Pravna podlaga", "Gostovanje", "Hramba podrobnih podatkov", "Hramba sej", "Anonimni agregati", "Različica obvestila"),
            ["hr"] = new("Voditelj obrade", "Kontakt", "Svrha", "Pravna osnova", "Hosting", "Čuvanje detaljnih podataka", "Čuvanje sesija", "Anonimni agregati", "Verzija obavijesti"),
            ["hu"] = new("Adatkezelő", "Kapcsolat", "Cél", "Jogalap", "Tárhely", "Részletes adatok megőrzése", "Munkamenetek megőrzése", "Névtelen összesítések", "Tájékoztató verziója"),
            ["ro"] = new("Operator", "Contact", "Scop", "Temei juridic", "Găzduire", "Păstrarea datelor detaliate", "Păstrarea sesiunilor", "Agregate anonime", "Versiunea informării"),
            ["bg"] = new("Администратор", "Контакт", "Цел", "Правно основание", "Хостинг", "Съхранение на подробни данни", "Съхранение на сесии", "Анонимни агрегати", "Версия на уведомлението"),
            ["el"] = new("Υπεύθυνος επεξεργασίας", "Επικοινωνία", "Σκοπός", "Νομική βάση", "Φιλοξενία", "Διατήρηση λεπτομερών δεδομένων", "Διατήρηση συνεδριών", "Ανώνυμα συγκεντρωτικά στοιχεία", "Έκδοση ενημέρωσης"),
            ["da"] = new("Dataansvarlig", "Kontakt", "Formål", "Retsgrundlag", "Hosting", "Opbevaring af detaljerede data", "Opbevaring af sessioner", "Anonyme aggregater", "Informationsversion"),
            ["sv"] = new("Personuppgiftsansvarig", "Kontakt", "Ändamål", "Rättslig grund", "Drift", "Lagring av detaljuppgifter", "Lagring av sessioner", "Anonyma aggregat", "Informationsversion"),
            ["nb"] = new("Behandlingsansvarlig", "Kontakt", "Formål", "Rettslig grunnlag", "Hosting", "Lagring av detaljdata", "Lagring av økter", "Anonyme aggregater", "Informasjonsversjon"),
            ["fi"] = new("Rekisterinpitäjä", "Yhteystieto", "Tarkoitus", "Oikeusperuste", "Hosting", "Yksityiskohtaisten tietojen säilytys", "Istuntojen säilytys", "Anonyymit koosteet", "Tietosuojaselosteen versio"),
            ["et"] = new("Vastutav töötleja", "Kontakt", "Eesmärk", "Õiguslik alus", "Majutus", "Üksikasjalike andmete säilitamine", "Seansside säilitamine", "Anonüümsed koondandmed", "Teabe versioon"),
            ["lv"] = new("Pārzinis", "Kontaktinformācija", "Nolūks", "Juridiskais pamats", "Mitināšana", "Detalizēto datu glabāšana", "Sesiju glabāšana", "Anonīmi apkopojumi", "Paziņojuma versija"),
            ["lt"] = new("Duomenų valdytojas", "Kontaktai", "Tikslas", "Teisinis pagrindas", "Priegloba", "Išsamių duomenų saugojimas", "Seansų saugojimas", "Anoniminės suvestinės", "Pranešimo versija"),
            ["ga"] = new("Rialaitheoir", "Teagmháil", "Cuspóir", "Bunús dlí", "Óstáil", "Coinneáil sonraí mionsonraithe", "Coinneáil seisiún", "Comhiomláin anaithnide", "Leagan an fhógra")
        };

    public static string CurrentLanguage
    {
        get
        {
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Texts.ContainsKey(language) ? language : "en";
        }
    }

    public static PrivacyText Current => Texts[CurrentLanguage];
    public static PrivacyLabels CurrentLabels => Labels[CurrentLanguage];

    internal static int SupportedLanguageCount => Texts.Count;
    internal static IEnumerable<string> SupportedLanguages => Texts.Keys;
    internal static int SupportedLabelCount => Labels.Count;
}
