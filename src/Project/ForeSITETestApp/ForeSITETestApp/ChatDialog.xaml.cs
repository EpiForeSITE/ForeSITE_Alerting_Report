using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using OllamaSharp;
using OllamaSharp.Models.Chat;



namespace ForeSITETestApp
{
    public partial class ChatDialog : Window
    {
        private ObservableCollection<MessageItem> messages = new ObservableCollection<MessageItem>();
        public string? LastUserMessage { get; private set; }

        public OllamaApiClient? OllamaClient { get; private set; }

        public Chat? Chat { get; private set; }

        public ChatDialog()
        {
            InitializeComponent();
            MessageList.ItemsSource = messages;
            messages.Add(new MessageItem { Sender = "Assistant", Text = "Please describe the chart you want." });

            string promptTemplate = @"
                                      You are an intelligent assistant to help understand user's input. Please identify its intent and key entities, returning the result in JSON format. The JSON structure is: 
                                      {""intent"":""<intent>"",
                                       ""entities"":{""<entity_name>"":""<value1>""},
                                       ""confidence"":<confidence_score, float between 0 and 1>
                                      } 
                                     Example:
                                     Input: ""I want to use FarringtonFlexible model to detect case, date range from 2019-01-01 to 2023-12-31 ""
                                     Output: {
                                         ""intent"": ""case_detect"",
                                         ""entities"":{
                                               ""model"": ""FarringtonFlexible"",
                                               ""begainDate"":""2019-01-01"",
                                               ""endDate"":""2023-12-31""
                                         },
                                         ""confidence"":0.95
                                     }
                                     Now, analyze the input and return the result in JSON format.
                                    
                         ";

            // Set up the client
            var uri = new Uri("http://localhost:11434");
            OllamaClient = new OllamaApiClient(uri);

            // Ensure OllamaClient is not null before proceeding
            if (OllamaClient != null)
            {
                // Select a model which should be used for further operations
                OllamaClient.SelectedModel = "deepseek-r1:7b";

                Chat = new Chat(OllamaClient, promptTemplate);
            }
        }

        private bool ValidateJson(string jsonString)
        {
            try
            {
                var jsonDoc = JsonDocument.Parse(jsonString);
                return jsonDoc.RootElement.TryGetProperty("intent", out _) &&
                       jsonDoc.RootElement.TryGetProperty("entities", out _) &&
                       jsonDoc.RootElement.TryGetProperty("confidence", out _);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private string ExtractJson(string input)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"\{[\s\S]*\}");
            return match.Success ? match.Value : input;
        }

      
        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string userMessage = InputTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(userMessage))
            {
                messages.Add(new MessageItem { Sender = "User", Text = userMessage });
                LastUserMessage = userMessage;
                messages.Add(new MessageItem { Sender = "Assistant", Text = "Thank you. I will generate the chart based on your description." });
                InputTextBox.Clear();

                // Call the Ollama API to generate a response
                if (OllamaClient != null && Chat!=null)
                {
                    try
                    {
                        // Use await with IAsyncEnumerable by iterating over the results
                        StringBuilder responseBuilder = new();
                        await foreach (var token in Chat.SendAsync(userMessage))
                        {
                            //messages.Add(new MessageItem { Sender = "Assistant", Text = token });
                            responseBuilder.Append(token);
                        }
                        string result = responseBuilder.ToString();
                       

                        if (string.IsNullOrEmpty(result))
                        {
                            messages.Add(new MessageItem { Sender = "Assistant", Text = "Empty response from LLM" });
                        }

                        // Validate JSON
                        if (ValidateJson(result))
                        {
                            messages.Add(new MessageItem { Sender = "Assistant", Text = result });
                        }
                        else
                        {
                            // Attempt to extract JSON if malformed
                            result = ExtractJson(result);
                            if (ValidateJson(result))
                            {
                                messages.Add(new MessageItem { Sender = "Assistant", Text = result });
                            }
                            else 
                                messages.Add(new MessageItem { Sender = "Assistant", Text = "Invalid JSON response" });
                        }
                    }
                    catch (Exception ex)
                    {
                        messages.Add(new MessageItem { Sender = "Assistant", Text = $"Error: {ex.Message}" });
                    }
                }
                else
                {
                    messages.Add(new MessageItem { Sender = "Assistant", Text = "Ollama client is not initialized." });
                }
            }
        }

        public class MessageItem
        {
            public string? Sender { get; set; }
            public string? Text { get; set; }
            public override string ToString()
            {
                return $"{Sender}: {Text}";
            }
        }
    }
}
