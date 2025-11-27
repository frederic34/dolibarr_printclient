using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json; // Nécessite System.Net.Http.Json
using System.Text.Json;

namespace PrintClient
{
    // 1. Modèle de données (doit correspondre à votre JSON API)
    public class PrintTask
    {
        public int Id { get; set; }
        public string FileName { get; set; }    // Nom du fichier pour sauvegarde locale
        public string Modulepart { get; set; } // modulepart
        public string JobId { get; set; }
        public int Status { get; set; }
    }

    public class DolibarrDownload
    {
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public string Filezise { get; set; }
        public string Content { get; set; }
        public string Encoding { get; set; }
    }
    class Program
    {
        // Configuration
        private static readonly HttpClient client = CreateConfiguredClient();

        private static HttpClient CreateConfiguredClient()
        {
            // 1. Gestion du SSL (Ignorer les erreurs)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            var httpClient = new HttpClient(handler);

            // 2. Ajouter l'entête "Accept: application/json"
            // Cela dit au serveur : "Renvoie-moi toujours du JSON s'il te plaît"
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            // (Optionnel) Si votre serveur a besoin d'un User-Agent spécifique
            httpClient.DefaultRequestHeaders.Add("User-Agent", "PrintClient v1.0");
            httpClient.DefaultRequestHeaders.Add("DOLAPIKEY", "GxIr1My2omME424S55JvGy1p3fnI6TCz");

            return httpClient;
        }
        private static readonly string ApiUrl = "http://host.docker.internal/api/index.php";
        private static readonly string TempFolder = Path.Combine(Path.GetTempPath(), "PrintJobs");

        static async Task Main(string[] args)
        {
            Console.WriteLine("--- Client d'Impression Démarré ---");

            // Créer le dossier temporaire si inexistant
            if (!Directory.Exists(TempFolder)) Directory.CreateDirectory(TempFolder);

            while (true)
            {
                try
                {
                    await CheckAndPrintTasks();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur générale : {ex.Message}");
                }

                // Attendre 10 secondes avant la prochaine vérification
                Console.WriteLine("Attente des prochaines tâches...");
                await Task.Delay(10000);
            }
        }

        static async Task CheckAndPrintTasks()
        {
            // 2. Récupérer les tâches depuis l'API
            // On suppose que l'API renvoie une liste : [ { "Id": 1, "FileUrl": "...", ... } ]
            Console.WriteLine("Vérification des tâches...");

            // Note: Ceci est un exemple, ajustez selon votre API réelle
            var tasks = await client.GetFromJsonAsync<List<PrintTask>>(ApiUrl + "/printjobapi/printjobs?sqlfilters=status%3A%3D%3A0");

            // POUR LE TEST : On simule une tâche
            // var tasks = new List<PrintTask>();
            // Décommentez la ligne ci-dessous pour tester avec une vraie API
            // tasks = await client.GetFromJsonAsync<List<PrintTask>>($"{ApiUrl}/pending");

            if (tasks == null || tasks.Count == 0) return;

            foreach (var task in tasks)
            {
                Console.WriteLine($"Traitement de la tâche ID: {task.Id}");

                // 3. Télécharger le fichier
                string localPath = Path.Combine(TempFolder, Path.GetFileName(task.FileName));
                Console.WriteLine(localPath);
                var url = ApiUrl + "/documents/download?original_file=" + task.FileName + "&modulepart=" + task.Modulepart;
                await DownloadFile(url, localPath);

                // 4. Envoyer à l'imprimante
                bool printed = PrintFile(localPath);

                if (printed)
                {
                    // 5. Confirmer au serveur que c'est fait
                    await ConfirmTaskComplete(task.Id);

                    // Nettoyage
                    if (File.Exists(localPath)) File.Delete(localPath);
                }
            }
        }

        static async Task DownloadFile(string url, string outputPath)
        {
            Console.WriteLine($"Téléchargement de {url}...");
            // var data = await client.GetByteArrayAsync(url);
            var download = await client.GetFromJsonAsync<DolibarrDownload>(url);
            // Console.WriteLine(download.Content);
            byte[] data = Convert.FromBase64String(download.Content);
            await File.WriteAllBytesAsync(outputPath, data);
        }

        static bool PrintFile(string filePath)
        {
            try
            {
                Console.WriteLine($"Envoi de {filePath} à CUPS...");

                // Configuration de la commande Linux 'lp'
                // lp -d NomDeLImprimante fichier.pdf (pour une imprimante spécifique)
                // lp fichier.pdf (pour l'imprimante par défaut)

                var info = new ProcessStartInfo
                {
                    FileName = "lp",
                    Arguments = $" -d AL-C2800 \"{filePath}\"", // Ajout de guillemets pour les espaces
                    RedirectStandardOutput = true,  // Pour lire la réponse de CUPS
                    RedirectStandardError = true,   // Pour lire les erreurs
                    UseShellExecute = false,        // Nécessaire sous Linux pour rediriger les flux
                    CreateNoWindow = true
                };

                // Si vous voulez viser une imprimante précise, décommentez et ajustez :
                // info.Arguments = $"-d ZEBRA_GK420t \"{filePath}\"";

                using (var p = Process.Start(info))
                {
                    // Lire la sortie (souvent l'ID du job d'impression)
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();

                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        Console.WriteLine($"Succès CUPS : {output.Trim()}");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Erreur CUPS : {error}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur critique : {ex.Message}");
                return false;
            }
        }

        static async Task ConfirmTaskComplete(int taskId)
        {
            Console.WriteLine($"Confirmation de la tâche {taskId} au serveur...");
            // Exemple d'appel POST/PUT pour mettre à jour le statut
            await client.PutAsync($"{ApiUrl}/printjobapi/printjobs/{taskId}?status=1", null);
        }
    }
}