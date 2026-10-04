using System.Globalization;
using System.Windows;

namespace Prism;

public static class L
{
    public static string Language {get;private set;}="en";
    // Each row is English | Hindi | Spanish | French. Provider names stay unchanged.
    static readonly Dictionary<string,string[]> Words=new(StringComparer.Ordinal);
    static L()
    {
        foreach(var row in Data.Split('\n',StringSplitOptions.RemoveEmptyEntries)){
            var fields=row.Trim().Split('|');if(fields.Length==4)Words[fields[0]]=fields;
        }
    }
    public static bool Known(string text)=>Words.ContainsKey(text);
    public static string T(string text)=>Words.TryGetValue(text,out var values)?values[Language switch{"hi"=>1,"es"=>2,"fr"=>3,_=>0}]:text;
    public static string F(string format,params object[] values)=>string.Format(CultureInfo.CurrentCulture,T(format),values);
    public static string Time(DateTimeOffset time,bool use24,bool compact=false)=>time.ToString(use24?"HH:mm":compact?"h:mmtt":"h:mm tt",compact?CultureInfo.InvariantCulture:CultureInfo.CurrentCulture);
    public static void Set(string language)
    {
        Language=new[]{"en","hi","es","fr"}.Contains(language)?language:"en";
        var culture=CultureInfo.GetCultureInfo(Language switch{"hi"=>"hi-IN","es"=>"es-ES","fr"=>"fr-FR",_=>"en-US"});
        CultureInfo.CurrentCulture=culture;CultureInfo.CurrentUICulture=culture;
        CultureInfo.DefaultThreadCurrentCulture=culture;CultureInfo.DefaultThreadCurrentUICulture=culture;
        if(Application.Current is not null)foreach(var key in Words.Keys)Application.Current.Resources["t."+key]=T(key);
    }
    const string Data="""
Providers|प्रदाता|Proveedores|Fournisseurs
Connections and providers|कनेक्शन और प्रदाता|Conexiones y proveedores|Connexions et fournisseurs
Read-only sources. Authentication stays with your provider.|केवल पढ़ने वाले स्रोत। साइन-इन प्रदाता संभालता है।|Fuentes de solo lectura. Su proveedor gestiona el acceso.|Sources en lecture seule. Le fournisseur gère la connexion.
Disabled|बंद|Desactivado|Désactivé
Disable provider|प्रदाता बंद करें|Desactivar proveedor|Désactiver le fournisseur
Enable provider|प्रदाता चालू करें|Activar proveedor|Activer le fournisseur
Enable or disable collection|रीडिंग चालू या बंद करें|Activar o desactivar lecturas|Activer ou désactiver la collecte
Last successful update: {0}|अंतिम सफल अपडेट: {0}|Última actualización correcta: {0}|Dernière mise à jour réussie : {0}
Every {0} seconds|हर {0} सेकंड|Cada {0} segundos|Toutes les {0} secondes
Remaining allowance|शेष सीमा|Cuota restante|Quota restant
Reset time|रीसेट समय|Hora de reinicio|Heure de réinitialisation
Multiple windows|कई कोटा अवधि|Varias ventanas|Plusieurs fenêtres
Local CLI|स्थानीय CLI|CLI local|CLI locale
API credits|API क्रेडिट|Créditos API|Crédits API
Allow existing OpenRouter environment sign-in|मौजूदा OpenRouter साइन-इन की अनुमति दें|Permitir la clave existente de OpenRouter|Autoriser la clé OpenRouter existante
Could not save settings. Check folder access.|सेटिंग्स सहेज नहीं पाए। फ़ोल्डर की पहुँच जाँचें।|No se pudieron guardar los ajustes. Revise el acceso a la carpeta.|Impossible d’enregistrer. Vérifiez l’accès au dossier.
No prompts, identities or machine information are sent to Prism servers. There are no Prism servers.|Prism का कोई सर्वर नहीं है। प्रॉम्प्ट, पहचान या कंप्यूटर की जानकारी नहीं भेजी जाती।|Prism no tiene servidores. No se envían solicitudes, identidades ni datos del equipo.|Prism n’a pas de serveur. Aucun prompt, identité ou détail de l’appareil n’y est envoyé.
System capacity|सिस्टम क्षमता|Capacidad del sistema|Capacité système
GPU|GPU|GPU|GPU
Dedicated VRAM|समर्पित VRAM|VRAM dedicada|VRAM dédiée
CPU frequency|CPU आवृत्ति|Frecuencia de CPU|Fréquence du CPU
Battery|बैटरी|Batería|Batterie
Disk read|डिस्क पढ़ना|Lectura del disco|Lecture disque
Disk write|डिस्क लिखना|Escritura del disco|Écriture disque
Current|वर्तमान|Actual|Actuel
Charging|चार्ज हो रहा है|Cargando|En charge
Discharging|बैटरी पर|Descargando|Sur batterie
Plugged in|पावर से जुड़ा|Conectado|Branché
Minimize to tray|ट्रे में छोटा करें|Minimizar a la bandeja|Réduire dans la zone de notification
Minimize to taskbar|टास्कबार में छोटा करें|Minimizar a la barra de tareas|Réduire dans la barre des tâches
Taskbar ribbon|टास्कबार रिबन|Cinta junto a la barra|Ruban près de la barre des tâches
Taskbar bar metric|टास्कबार बार का मेट्रिक|Métrica de barra de tareas|Mesure de la barre des tâches
None|कोई नहीं|Ninguna|Aucune
Only active AI subscriptions|केवल सक्रिय AI सदस्यता|Solo suscripciones de IA activas|Abonnements IA actifs uniquement
Active means a provider has quota windows. Prism cannot verify paid subscription entitlement. Stale readings remain marked stale.|सक्रिय का अर्थ उपलब्ध कोटा है। Prism भुगतान वाली सदस्यता की पुष्टि नहीं करता। पुरानी रीडिंग चिन्हित रहती हैं।|Activo significa que el proveedor muestra cuotas. Prism no verifica suscripciones de pago. Los datos antiguos siguen marcados.|Actif signifie que le fournisseur expose des quotas. Prism ne vérifie pas les abonnements payants. Les relevés périmés restent signalés.
Show all metrics|सभी मेट्रिक दिखाएँ|Mostrar todas las métricas|Afficher toutes les mesures
Hide all metrics|सभी मेट्रिक छिपाएँ|Ocultar todas las métricas|Masquer toutes les mesures
No metrics selected|कोई मेट्रिक नहीं चुना|Sin métricas seleccionadas|Aucune mesure sélectionnée
No metrics selected. Open Settings to choose metrics.|कोई मेट्रिक नहीं चुना। सेटिंग्स में चुनें।|Sin métricas seleccionadas. Abra Ajustes para elegirlas.|Aucune mesure sélectionnée. Choisissez-les dans les paramètres.
Windows-reported values. Unsupported sensors stay unavailable.|Windows द्वारा दिए आँकड़े। असमर्थित सेंसर अनुपलब्ध रहते हैं।|Valores de Windows. Los sensores no compatibles no están disponibles.|Valeurs fournies par Windows. Les capteurs non pris en charge restent indisponibles.
GPU metrics unavailable|GPU मेट्रिक अनुपलब्ध|Métricas de GPU no disponibles|Mesures GPU indisponibles
Top resource consumers|अधिक संसाधन उपयोग करने वाले ऐप|Procesos que más consumen|Processus les plus gourmands
Process names and current usage stay local and are never saved.|प्रक्रिया नाम और उपयोग स्थानीय रहते हैं और सहेजे नहीं जाते।|Los nombres y el uso de procesos son locales y no se guardan.|Les noms et l’usage des processus restent locaux et ne sont pas enregistrés.
Open Task Manager|टास्क मैनेजर खोलें|Abrir Administrador de tareas|Ouvrir le Gestionnaire des tâches
Temperature, package power and battery health are unavailable without dependable hardware support.|विश्वसनीय हार्डवेयर समर्थन के बिना तापमान, ऊर्जा और बैटरी स्वास्थ्य अनुपलब्ध हैं।|La temperatura, potencia y salud de batería requieren soporte fiable del hardware.|La température, la puissance et l’état de la batterie nécessitent un support matériel fiable.
History stores quota windows and opaque account scopes locally for 30 days. Disabling recording preserves saved history.|इतिहास में कोटा अवधि और अपारदर्शी खाता पहचान 30 दिन स्थानीय रहती हैं। रिकॉर्डिंग रोकने से पुराना इतिहास नहीं मिटता।|El historial guarda cuotas e identificadores opacos locales durante 30 días. Desactivar el registro conserva los datos guardados.|L’historique conserve localement les quotas et des identifiants opaques pendant 30 jours. Désactiver l’enregistrement conserve les données.
Capacity|क्षमता|Capacidad|Capacité
Captured: {0}|रीडिंग का समय: {0}|Capturado: {0}|Relevé : {0}
Critical capacity|बहुत कम क्षमता|Capacidad crítica|Capacité critique
Low capacity|कम क्षमता|Capacidad baja|Capacité faible
{0:0.#} days|{0:0.#} दिन|{0:0.#} días|{0:0.#} jours
{0:0.#} hours|{0:0.#} घंटे|{0:0.#} horas|{0:0.#} heures
Capacity & resets|क्षमता और रीसेट|Capacidad y reinicios|Capacité et réinitialisations
Next resets|अगले रीसेट|Próximos reinicios|Prochaines réinitialisations
Provider readings and local estimates. Estimates are not guarantees.|प्रदाता की रीडिंग और स्थानीय अनुमान। अनुमान निश्चित नहीं हैं।|Datos del proveedor y estimaciones locales. No son garantías.|Relevés du fournisseur et estimations locales. Sans garantie.
No current quota windows|कोई वर्तमान कोटा अवधि नहीं|Sin períodos de cuota actuales|Aucune période de quota actuelle
Unknown reset|रीसेट अज्ञात|Reinicio desconocido|Réinitialisation inconnue
Limiting window|सीमित करने वाली अवधि|Período limitante|Période limitante
{0:0} minutes|{0:0} मिनट|{0:0} minutos|{0:0} minutes
Stable estimate|स्थिर अनुमान|Estimación estable|Estimation stable
Insufficient history|अपर्याप्त इतिहास|Historial insuficiente|Historique insuffisant
Highly variable usage|उपयोग में अधिक बदलाव|Uso muy variable|Utilisation très variable
Account scope unavailable|खाता पहचान उपलब्ध नहीं|Ámbito de cuenta no disponible|Périmètre du compte indisponible
Rolling allowance|निरंतर बदलता कोटा|Cuota renovable|Quota glissant
Reset imminent|रीसेट निकट है|Reinicio inminente|Réinitialisation imminente
Recent pace: ~{0:0.#}% / hour|हाल की दर: ~{0:0.#}% / घंटा|Ritmo reciente: ~{0:0.#}% / hora|Rythme récent : ~{0:0.#}% / heure
Safe pace: ~{0:0.#}% / hour|टिकाऊ दर: ~{0:0.#}% / घंटा|Ritmo sostenible: ~{0:0.#}% / hora|Rythme soutenable : ~{0:0.#}% / heure
Projected at reset: ~{0:0.#}% remaining|रीसेट पर अनुमान: ~{0:0.#}% शेष|Proyección al reinicio: ~{0:0.#}% restante|Projection au renouvellement : ~{0:0.#}% restant
Estimated exhaustion: {0}|समाप्ति का अनुमान: {0}|Agotamiento estimado: {0}|Épuisement estimé : {0}
Capacity estimate|क्षमता का अनुमान|Estimación de capacidad|Estimation de capacité
{0} may exhaust before reset at the recent pace.|हाल की दर पर {0} रीसेट से पहले समाप्त हो सकता है।|{0} podría agotarse antes del reinicio al ritmo reciente.|Au rythme récent, {0} pourrait s’épuiser avant le renouvellement.
Show capacity estimates|क्षमता अनुमान दिखाएँ|Mostrar estimaciones|Afficher les estimations
Enable predictive alerts|अनुमान आधारित अलर्ट सक्षम करें|Activar alertas predictivas|Activer les alertes prédictives
Quota window|कोटा अवधि|Período de cuota|Période de quota
Legacy aggregate|पुराना संयुक्त इतिहास|Historial agregado anterior|Ancien historique agrégé
Show projected trajectory|अनुमानित रुझान दिखाएँ|Mostrar trayectoria estimada|Afficher la trajectoire estimée
Estimate|अनुमान|Estimación|Estimation
Current|वर्तमान|Actual|Actuel
Reset|रीसेट|Reinicio|Réinitialisation
Settings|सेटिंग्स|Ajustes|Réglages
Connect this source to see its allowance.|कोटा देखने के लिए इस स्रोत से जुड़ें।|Conecte esta fuente para ver su cuota.|Connectez cette source pour voir son quota.
No current quota window · waiting for a fresh reading|कोई ताज़ा कोटा नहीं · नई रीडिंग की प्रतीक्षा|Sin cuota actual · esperando datos nuevos|Aucun quota actuel · attente d’un nouveau relevé
Recent|हाल का|Reciente|Récent
Close Prism|Prism बंद करें|Cerrar Prism|Fermer Prism
More controls|और नियंत्रण|Más controles|Autres commandes
Settings · accounts, clocks and reminders|सेटिंग्स · खाते, घड़ियाँ और अनुस्मारक|Ajustes · cuentas, relojes y avisos|Réglages · comptes, horloges et rappels
Free RAM · trim selected apps without closing them|रैम खाली करें · ऐप बंद किए बिना|Liberar RAM · sin cerrar las aplicaciones|Libérer RAM · sans fermer les applications
Refresh AI readings · F5|AI रीडिंग रिफ्रेश करें · F5|Actualizar lecturas IA · F5|Actualiser les relevés IA · F5
Fit height · fit the widget to its contents|ऊँचाई सामग्री के अनुसार करें|Ajustar altura al contenido|Ajuster la hauteur au contenu
Free RAM|रैम खाली करें|Liberar RAM|Libérer RAM
Refresh|रिफ्रेश|Actualizar|Actualiser
General|सामान्य|General|Général
Refresh rates|रिफ्रेश अंतराल|Frecuencia|Fréquence
Metrics|मेट्रिक्स|Métricas|Mesures
Alerts|अलर्ट|Alertas|Alertes
Profiles|प्रोफ़ाइल|Perfiles|Profils
Updates|अपडेट|Actualizaciones|Mises à jour
Save|सहेजें|Guardar|Enregistrer
Cancel|रद्द करें|Cancelar|Annuler
Close|बंद करें|Cerrar|Fermer
Language|भाषा|Idioma|Langue
Use 24-hour time|24 घंटे का समय|Formato de 24 horas|Format 24 heures
Enable system tray|सिस्टम ट्रे सक्षम करें|Activar bandeja del sistema|Activer la zone de notification
Start with Windows|Windows के साथ शुरू करें|Iniciar con Windows|Démarrer avec Windows
Snap to screen edges|स्क्रीन किनारों से जोड़ें|Ajustar a los bordes|Aligner sur les bords
Opacity|अपारदर्शिता|Opacidad|Opacité
Save local quota history|कोटा इतिहास स्थानीय सहेजें|Guardar historial local|Enregistrer l’historique local
Enable low-quota alerts|कम कोटा अलर्ट सक्षम करें|Alertas de cuota baja|Alertes de quota faible
Remaining threshold (%)|शेष सीमा (%)|Umbral restante (%)|Seuil restant (%)
Move up|ऊपर करें|Subir|Monter
Move down|नीचे करें|Bajar|Descendre
At least one metric is required.|कम से कम एक मेट्रिक आवश्यक है।|Se requiere al menos una métrica.|Une mesure au minimum est requise.
Save current layout|वर्तमान लेआउट सहेजें|Guardar diseño actual|Enregistrer la disposition
Load profile|प्रोफ़ाइल लोड करें|Cargar perfil|Charger le profil
Delete profile|प्रोफ़ाइल हटाएँ|Eliminar perfil|Supprimer le profil
Profile name|प्रोफ़ाइल नाम|Nombre del perfil|Nom du profil
Check for updates|अपडेट जाँचें|Buscar actualizaciones|Rechercher des mises à jour
Download verified ZIP|सत्यापित ZIP डाउनलोड करें|Descargar ZIP verificado|Télécharger le ZIP vérifié
Open downloads folder|डाउनलोड फ़ोल्डर खोलें|Abrir carpeta de descargas|Ouvrir les téléchargements
Checking…|जाँच जारी…|Comprobando…|Vérification…
Downloading and verifying…|डाउनलोड और सत्यापन जारी…|Descargando y verificando…|Téléchargement et vérification…
You are up to date.|आपका संस्करण अद्यतन है।|Está actualizado.|Vous êtes à jour.
Verified download ready. Extract it into a new folder and run Setup.cmd.|सत्यापित डाउनलोड तैयार है। नए फ़ोल्डर में निकालें और Setup.cmd चलाएँ।|Descarga verificada. Extraiga en una carpeta nueva y ejecute Setup.cmd.|Téléchargement vérifié. Extrayez dans un nouveau dossier et lancez Setup.cmd.
Update check failed. Please try again later.|अपडेट जाँच विफल। बाद में पुनः प्रयास करें।|No se pudo comprobar. Inténtelo más tarde.|Échec de la vérification. Réessayez plus tard.
Download failed or could not be verified.|डाउनलोड विफल या सत्यापन असफल।|Descarga fallida o no verificada.|Téléchargement échoué ou non vérifié.
Prism {0} is available ({1:0} MB).|Prism {0} उपलब्ध है ({1:0} MB)।|Prism {0} disponible ({1:0} MB).|Prism {0} disponible ({1:0} Mo).
Usage history|उपयोग इतिहास|Historial de uso|Historique d’utilisation
24 hours|24 घंटे|24 horas|24 heures
7 days|7 दिन|7 días|7 jours
30 days|30 दिन|30 días|30 jours
Clear history|इतिहास साफ करें|Borrar historial|Effacer l’historique
Clear the local quota history? This cannot be undone.|स्थानीय कोटा इतिहास साफ करें? इसे पूर्ववत नहीं किया जा सकता।|¿Borrar el historial local? No se puede deshacer.|Effacer l’historique local ? Cette action est irréversible.
No readings yet. History fills as fresh quota arrives.|अभी रीडिंग नहीं है। ताज़ा कोटा आने पर इतिहास भरेगा।|Sin lecturas aún. El historial se llenará con datos nuevos.|Aucune donnée. L’historique se remplira au fil des relevés.
Remaining allowance (%)|शेष कोटा (%)|Cuota restante (%)|Quota restant (%)
{0} samples · latest {1:0.#}%|{0} नमूने · नवीनतम {1:0.#}%|{0} muestras · último {1:0.#}%|{0} relevés · dernier {1:0.#}%
Show Prism|Prism दिखाएँ|Mostrar Prism|Afficher Prism
Hide to tray|ट्रे में छिपाएँ|Ocultar en la bandeja|Masquer dans la zone de notification
Exit Prism|Prism बंद करें|Salir de Prism|Quitter Prism
Low quota|कम कोटा|Cuota baja|Quota faible
Quota resets soon|कोटा जल्द रीसेट होगा|La cuota se restablece pronto|Réinitialisation du quota proche
Next check: {0}|अगली जाँच: {0}|Próxima consulta: {0}|Prochaine vérification : {0}
{0} of {1} sources current|{1} में से {0} स्रोत ताज़ा|{0} de {1} fuentes al día|{0} sources sur {1} à jour
Refresh failed; scheduled checks will retry.|रिफ्रेश विफल; निर्धारित जाँच पुनः प्रयास करेगी।|Actualización fallida; se reintentará.|Échec ; nouvelle tentative planifiée.
Workspace pulse|कार्यस्थल की स्थिति|Estado del espacio|État de l’espace
Live system health. AI capacity in view.|सिस्टम की स्थिति और AI क्षमता।|Estado del sistema y capacidad IA.|État du système et capacité IA.
PROCESSOR|प्रोसेसर|PROCESADOR|PROCESSEUR
MEMORY|मेमोरी|MEMORIA|MÉMOIRE
SYSTEM DRIVE|सिस्टम ड्राइव|DISCO DEL SISTEMA|DISQUE SYSTÈME
NETWORK · ↓ / ↑|नेटवर्क · ↓ / ↑|RED · ↓ / ↑|RÉSEAU · ↓ / ↑
AI CAPACITY|AI क्षमता|CAPACIDAD IA|CAPACITÉ IA
CPU|सीपीयू|CPU|CPU
Memory|मेमोरी|Memoria|Mémoire
Free disk|खाली डिस्क|Disco libre|Disque libre
Network|नेटवर्क|Red|Réseau
Keep on top|सबसे ऊपर रखें|Siempre visible|Toujours au premier plan
Unpin widget|पिन हटाएँ|Desanclar|Détacher
Fit to content|सामग्री के अनुसार आकार|Ajustar al contenido|Ajuster au contenu
Toggle compact view|कॉम्पैक्ट दृश्य बदलें|Vista compacta|Vue compacte
Connections…|कनेक्शन…|Conexiones…|Connexions…
World clocks…|विश्व घड़ियाँ…|Relojes mundiales…|Horloges mondiales…
Reset reminders…|रीसेट अनुस्मारक…|Avisos de reinicio…|Rappels de réinitialisation…
Diagnostics…|निदान…|Diagnóstico…|Diagnostic…
{0:0.#}% left|{0:0.#}% शेष|{0:0.#}% restante|{0:0.#}% restant
{0:0.0} GB free|{0:0.0} GB खाली|{0:0.0} GB libres|{0:0.0} Go libres
Live|ताज़ा|En vivo|Actuel
Stale|पुराना|Desactualizado|Périmé
Connect|कनेक्ट करें|Conectar|Connecter
Waiting|प्रतीक्षा|Esperando|En attente
Unavailable|अनुपलब्ध|No disponible|Indisponible
just now|अभी|ahora|à l’instant
{0:0}m ago|{0:0} मिनट पहले|hace {0:0} min|il y a {0:0} min
{0:0}h ago|{0:0} घंटे पहले|hace {0:0} h|il y a {0:0} h
Resets in {0}d|{0} दिन में रीसेट|Reinicio en {0} d|Réinitialisation dans {0} j
Resets in {0}h|{0} घंटे में रीसेट|Reinicio en {0} h|Réinitialisation dans {0} h
Resets in {0}m|{0} मिनट में रीसेट|Reinicio en {0} min|Réinitialisation dans {0} min
Show two additional timezone clocks|दो अतिरिक्त समयक्षेत्र घड़ियाँ दिखाएँ|Mostrar dos relojes adicionales|Afficher deux horloges supplémentaires
Save clocks|घड़ियाँ सहेजें|Guardar relojes|Enregistrer les horloges
Enable reset reminders|रीसेट अनुस्मारक सक्षम करें|Activar avisos de reinicio|Activer les rappels
Save reminders|अनुस्मारक सहेजें|Guardar avisos|Enregistrer les rappels
Preview reminder|अनुस्मारक पूर्वावलोकन|Vista previa del aviso|Aperçu du rappel
Copy report|रिपोर्ट कॉपी करें|Copiar informe|Copier le rapport
due now|अभी जाँच|ahora|maintenant
Saved.|सहेजा गया।|Guardado.|Enregistré.
Could not save settings.|सेटिंग्स सहेजी नहीं जा सकीं।|No se pudieron guardar los ajustes.|Impossible d’enregistrer les réglages.
Enter a profile name.|प्रोफ़ाइल का नाम लिखें।|Introduzca un nombre.|Saisissez un nom de profil.
Maximum 20 profiles.|अधिकतम 20 प्रोफ़ाइल।|Máximo 20 perfiles.|20 profils maximum.
Could not save profile.|प्रोफ़ाइल सहेजी नहीं जा सकी।|No se pudo guardar el perfil.|Impossible d’enregistrer le profil.
System tray is unavailable. Prism will stay visible.|सिस्टम ट्रे उपलब्ध नहीं है। Prism दिखाई देगा।|Bandeja no disponible. Prism seguirá visible.|Zone de notification indisponible. Prism restera visible.
Could not save settings. Check folder access or an existing startup registration.|सेटिंग्स सहेजी नहीं जा सकीं। फ़ोल्डर की अनुमति या मौजूदा स्टार्टअप प्रविष्टि जाँचें।|No se pudo guardar. Compruebe los permisos de carpeta o el registro de inicio existente.|Enregistrement impossible. Vérifiez les droits du dossier ou l’entrée de démarrage existante.
Startup is optional and uses only your Windows account. Closing Prism exits; Hide to tray keeps monitoring.|स्टार्टअप वैकल्पिक है और केवल आपका Windows खाता उपयोग करता है। बंद करने पर Prism रुकेगा; ट्रे में छिपाने पर निगरानी जारी रहेगी।|El inicio automático es opcional y solo usa su cuenta de Windows. Cerrar Prism termina la aplicación; ocultarlo mantiene la supervisión.|Le démarrage automatique est facultatif et propre à votre compte Windows. Fermer Prism quitte l’application ; la masquer maintient le suivi.
Provider caches and rate limits still apply. Hover a provider or the footer for its next check.|प्रदाता की कैश और दर सीमाएँ लागू रहती हैं। अगली जाँच देखने के लिए प्रदाता या नीचे की पट्टी पर माउस रखें।|Se siguen aplicando las cachés y límites del proveedor. Pase el cursor sobre un proveedor o el pie para ver la próxima consulta.|Les caches et limites du fournisseur s’appliquent. Survolez un fournisseur ou le pied de page pour voir la prochaine vérification.
Choose visible metrics and their order. The compact grid uses the full order; the dashboard keeps paired system groups and orders AI cards.|दिखने वाले मेट्रिक्स और उनका क्रम चुनें। कॉम्पैक्ट ग्रिड पूरा क्रम अपनाता है; डैशबोर्ड सिस्टम के जोड़े रखता है और AI कार्ड क्रमबद्ध करता है।|Elija las métricas visibles y su orden. La cuadrícula compacta respeta todo el orden; el panel conserva los grupos del sistema y ordena las tarjetas IA.|Choisissez les mesures visibles et leur ordre. La grille compacte suit cet ordre ; le tableau conserve les paires système et trie les cartes IA.
Each low-quota window alerts once until it recovers or resets. Background alerts use Windows notifications; your notification settings may suppress them.|हर कम-कोटा अवधि में एक बार अलर्ट आता है, जब तक कोटा सुधरे या रीसेट हो। पृष्ठभूमि अलर्ट Windows सूचनाओं से आते हैं; आपकी सेटिंग्स उन्हें रोक सकती हैं।|Cada período avisa una vez hasta recuperarse o reiniciarse. Las alertas en segundo plano usan notificaciones de Windows, que sus ajustes pueden bloquear.|Chaque période alerte une fois jusqu’au rétablissement ou à la réinitialisation. Les alertes utilisent les notifications Windows, selon vos réglages.
History stores only provider, timestamp and remaining percentage for 30 days. Disabling recording preserves existing history until you clear it.|इतिहास में केवल प्रदाता, समय और शेष प्रतिशत 30 दिन तक सहेजे जाते हैं। रिकॉर्डिंग बंद करने से मौजूदा इतिहास नहीं मिटता।|El historial guarda solo proveedor, fecha y porcentaje restante durante 30 días. Desactivar el registro conserva el historial hasta borrarlo.|L’historique conserve uniquement le fournisseur, la date et le pourcentage restant pendant 30 jours. Désactiver l’enregistrement ne l’efface pas.
Profiles save size, density, pinning, opacity, visible metrics, order and clocks. They never include account credentials.|प्रोफ़ाइल आकार, घनत्व, पिन, अपारदर्शिता, मेट्रिक्स, क्रम और घड़ियाँ सहेजती हैं। इनमें खाते की साइन-इन जानकारी नहीं होती।|Los perfiles guardan tamaño, densidad, anclaje, opacidad, métricas, orden y relojes. Nunca incluyen credenciales.|Les profils enregistrent taille, densité, épinglage, opacité, mesures, ordre et horloges. Ils ne contiennent jamais d’identifiants.
Checks contact GitHub only when requested. ZIPs are verified against the published SHA-256 checksum. Binaries remain unsigned; downloads never install or replace files automatically.|जाँच केवल आपके अनुरोध पर GitHub से संपर्क करती है। ZIP का प्रकाशित SHA-256 से सत्यापन होता है। ऐप अभी बिना डिजिटल हस्ताक्षर के है; डाउनलोड स्वतः इंस्टॉल या फ़ाइलें नहीं बदलते।|GitHub solo se consulta a petición. Los ZIP se verifican con el SHA-256 publicado. Los binarios no están firmados; las descargas no instalan ni reemplazan archivos automáticamente.|GitHub est contacté uniquement sur demande. Les ZIP sont vérifiés avec le SHA-256 publié. Les exécutables ne sont pas signés ; aucun fichier n’est installé ou remplacé automatiquement.
SYSTEMS  /  INTELLIGENCE|सिस्टम  /  इंटेलिजेंस|SISTEMA  /  INTELIGENCIA|SYSTÈME  /  INTELLIGENCE
Active physical adapters|सक्रिय नेटवर्क एडेप्टर|Adaptadores físicos activos|Cartes réseau actives
System live · AI readings carry their own timestamp|सिस्टम ताज़ा · AI रीडिंग का अपना समय है|Sistema en vivo · cada lectura IA tiene su fecha|Système en direct · chaque relevé IA est horodaté
Reading memory|मेमोरी पढ़ रहे हैं|Leyendo memoria|Lecture de la mémoire
Connecting to your workspace|कार्यस्थल से जुड़ रहे हैं|Conectando al espacio|Connexion à votre espace
Connecting…|जुड़ रहे हैं…|Conectando…|Connexion…
WAITING|प्रतीक्षा|ESPERANDO|EN ATTENTE
Network download / upload|नेटवर्क डाउनलोड / अपलोड|Descarga / subida de red|Téléchargement / envoi réseau
A nudge before the reset|रीसेट से पहले याद दिलाएँ|Un aviso antes del reinicio|Un rappel avant la réinitialisation
Remind me before reset|रीसेट से पहले याद दिलाएँ|Avisar antes del reinicio|Me rappeler avant la réinitialisation
Only current readings with a known reset time qualify. Prism must be running, either visible or in the system tray.|केवल ज्ञात रीसेट समय वाली ताज़ा रीडिंग योग्य हैं। Prism दिखाई देते हुए या सिस्टम ट्रे में चलता रहना चाहिए।|Solo se usan lecturas actuales con reinicio conocido. Prism debe estar ejecutándose, visible o en la bandeja.|Seuls les relevés actuels avec réinitialisation connue sont utilisés. Prism doit fonctionner, visible ou dans la zone de notification.
Two places. One glance.|दो जगहें। एक नज़र।|Dos lugares. Un vistazo.|Deux lieux. Un regard.
Add two clocks alongside your local time. Daylight saving is handled by Windows. Hover a clock for its date and UTC offset.|स्थानीय समय के साथ दो घड़ियाँ जोड़ें। ग्रीष्मकालीन समय Windows सँभालता है। तारीख और UTC अंतर के लिए घड़ी पर माउस रखें।|Añada dos relojes a su hora local. Windows gestiona el horario de verano. Pase el cursor para ver fecha y desfase UTC.|Ajoutez deux horloges à l’heure locale. Windows gère l’heure d’été. Survolez pour voir la date et le décalage UTC.
Show a quiet, 12-second reminder when a quota window is about to reset and more than 10% remains. Each window alerts once per reset, even across restarts.|कोटा रीसेट होने वाला हो और 10% से अधिक शेष हो तो 12 सेकंड का शांत अनुस्मारक दिखाएँ। हर अवधि में एक बार अलर्ट, ऐप पुनः खोलने पर भी।|Muestra un aviso discreto de 12 segundos antes del reinicio si queda más del 10%. Cada período avisa una vez, incluso tras reiniciar la aplicación.|Affiche un rappel discret de 12 secondes avant une réinitialisation s’il reste plus de 10 %. Une alerte par période, même après redémarrage.
""";
}
