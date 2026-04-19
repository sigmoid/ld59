using System.Text.RegularExpressions;

public class MainClass
{
    public static void Main(string[] args)
    {
        string inputFileContent;
        // the first argument is a relative path to a text file
        if (args.Length > 0)
        {
            var filePath = args[0];
            if (File.Exists(filePath))
            {
                inputFileContent = File.ReadAllText(filePath);
            }
            else
            {
                Console.WriteLine($"File not found: {filePath}");
                return;
            }
        }
        else
        {
            Console.WriteLine("Please provide a relative path to a text file as the first argument.");
            return;
        }

        // the second argument is a path to a file that provides mappings for encoding
        if (args.Length > 1)
        {
            var mappingFilePath = args[1];
            if (File.Exists(mappingFilePath))
            {
                var mappings = File.ReadAllLines(mappingFilePath)
                    .Select(line => line.Split('='))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

                Console.WriteLine("Mappings loaded: there are " + mappings.Count + " mappings.");

                var pattern = string.Join("|", mappings.Keys.Select(Regex.Escape));
                int replacements = 0;
                inputFileContent = Regex.Replace(inputFileContent, pattern, m =>
                {
                    replacements++;
                    return mappings[m.Value];
                });
                Console.WriteLine($"Encoding complete. Total replacements made: {replacements}");
            }
            else
            {
                Console.WriteLine($"Mapping file not found: {mappingFilePath}");
                return;
            }
        }
        else
        {
            Console.WriteLine("Please provide a path to a mapping file as the second argument.");
        }

        // the third argument is a name for the output file
        string outputFileName = "encoded_output.txt";
        if (args.Length > 2)
        {
            outputFileName = args[2];
        }

        File.WriteAllText(outputFileName, inputFileContent);
        Console.WriteLine($"File encoded and saved as: {outputFileName}");
    }
}