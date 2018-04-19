﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using System.IO;

namespace Server
{
    class Program
    {
        /// <summary>Objeto para controlar o bloqueio da filaComandos </summary>
        static readonly object blockComandos = new object();
        /// <summary>Objeto para controlar o bloqueio da filaProcessa </summary>
        static readonly object blockProcessa = new object();
        /// <summary>Objeto para controlar o bloqueio da filaLog </summary>
        static readonly object blockLog = new object();
        
        /// <summary>Fila de comandos recebidos</summary>
        static Queue<Requisicao> filaComandos = new Queue<Requisicao>();
        /// <summary>Fila de comandos para serem processados</summary>
        static Queue<Requisicao> filaProcessa = new Queue<Requisicao>();
        /// <summary>Fila de comandos que serão gravados no log</summary>
        static Queue<Requisicao> filaLog = new Queue<Requisicao>();

        /// <summary>Mapa</summary>
        static Dictionary<long, String> Mapa = new Dictionary<long, string>();

        /// <summary>
        /// Thread principal do servidor, cria as outras threads e é responsável por receber os comandos e os escrever em filaComandos.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            //Socket
            int receivedDataLength;
            byte[] data = new byte[1400];
            IPEndPoint ip = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1500);

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            socket.Bind(ip);

            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            EndPoint Remote = (EndPoint)(sender);

            RecuperaMapa();

            //Threads
            Task threadComandos = new Task(ThreadComandos);
            Task threadProcessaComando = new Task(ThreadProcessaComando);
            Task threadLogaDisco = new Task(ThreadLogaDisco);

            threadComandos.Start();
            threadProcessaComando.Start();
            threadLogaDisco.Start();
            
            while (true)
            {
                data = new byte[1400];
                receivedDataLength = socket.ReceiveFrom(data, ref Remote);

                string json = Encoding.ASCII.GetString(data, 0, receivedDataLength);
                Comando comando = JsonConvert.DeserializeObject<Comando>(json);
                Requisicao req = new Requisicao(Remote, comando);

                lock (blockComandos)
                {
                    filaComandos.Enqueue(req);
                }
            }
        }

        static public void RecuperaMapa()
        {
            Comando comando;
            string resposta = "";
            if (!File.Exists("json.txt"))
            {
                var stream = File.Create("json.txt");
                stream.Close();
            }
            using (StreamReader file = new StreamReader("json.txt"))
            {
                while(!file.EndOfStream)
                {
                    comando = JsonConvert.DeserializeObject<Comando>(file.ReadLine());
                    ProcessaComando(comando, ref resposta);
                }
                file.Close();
            }
        }

        static public void ProcessaComando(Comando comando, ref string resposta)
        {
            switch (comando.comand)
            {
                case (int)Comandos.ADD:
                    if (Mapa.ContainsKey(comando.Chave))
                    {
                        resposta = "Não foi possível inserir o item, chave já existente.";
                    }
                    else
                    {
                        resposta = "Inserido com sucesso.";
                        Mapa.Add(comando.Chave, comando.Valor);
                    }
                    break;

                case (int)Comandos.UPDATE:

                    if (Mapa.ContainsKey(comando.Chave))
                    {
                        resposta = "Atualizacao efetuada com sucesso.";
                        Mapa[comando.Chave] = comando.Valor;
                    }
                    else
                    {
                        resposta = "Não foi possível atualizar, elemento inexistente.";
                    }
                    break;

                case (int)Comandos.READ:
                    string data;

                    if (!Mapa.TryGetValue(comando.Chave, out data))
                    {
                        resposta = "Chave nao encontrada, elemento inexistente.";
                    }
                    else
                    {
                        resposta = data;
                    }
                    break;

                case (int)Comandos.DELETE:
                    if (Mapa.ContainsKey(comando.Chave))
                    {
                        Mapa.Remove(comando.Chave);
                        resposta = "Remocao efetuada com sucesso.";
                    }
                    else
                    {
                        resposta = "Não foi possível remover, elemento inexistente.";
                    }
                    break;
            }
        }

        /// <summary>
        /// Thread que pega os comandos de filaComandos e os escreve em filaProcessa e filaLog.
        /// </summary>
        static void ThreadComandos()
        {
            Requisicao req;
            while (true)
            {
                lock (blockComandos)
                {
                    if (filaComandos.Count > 0)
                    {
                        req = filaComandos.Dequeue();
                        lock (blockProcessa)
                        {
                            filaProcessa.Enqueue(req);
                        }

                        lock (blockLog)
                        {
                            filaLog.Enqueue(req);
                        }
                    }
                }
                
            }
        }

        /// <summary>
        /// Thread que pega os comandos de filaLog e os escreve em disco.
        /// </summary>
        static void ThreadLogaDisco()
        {
            Requisicao req;
            while (true)
            {
                lock (blockLog)
                {
                    using (StreamWriter file = new StreamWriter("json.txt", true))
                    {
                        while (filaLog.Count > 0)
                        {
                            req = filaLog.Dequeue();
                            if (req.Comand.comand == (int)Comandos.READ)
                                continue;
                            string comando = JsonConvert.SerializeObject(req.Comand);
                            file.WriteLine(comando);
                        }
                        file.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Thread que pega os comandos de filaProcessa os processa e envia o resultado para o cliente.
        /// </summary>
        static void ThreadProcessaComando()
        {
            Requisicao req;
            IPEndPoint ip = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1600);

            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            socket.Bind(ip);

            while (true)
            {
                lock (blockProcessa)
                {
                    if (filaProcessa.Count > 0)
                    {
                        req = filaProcessa.Dequeue();
                        string resposta = "";
                        byte[] resp;
                        ProcessaComando(req.Comand, ref resposta);
                        resp = Encoding.ASCII.GetBytes(resposta);
                        socket.SendTo(resp, resposta.Length, SocketFlags.None, req.Remote);
                    }
                }
            }
        }
    }
}
