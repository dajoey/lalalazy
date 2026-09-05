using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace LazyFishSitter;

/*
 * Trimmed copy of XivCommon's Chat.cs, MIT License, Copyright (c) 2021 Anna Clemens
 * (https://git.annaclemens.io/ascclemens/XivCommon). The same code path ECommons
 * ships inside this repo at src/GluttonyCombo/ECommons/ECommons/Automation/Chat.cs;
 * only the "type a slash command into the chat entry" route is kept here so this
 * plugin stays a single DLL with no NuGet dependency.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 */
internal static unsafe class ChatCommand
{
    /// <summary>Executes a slash command exactly as if it was typed into the chat box.</summary>
    internal static void Execute(string command)
    {
        if (!command.StartsWith("/"))
            throw new ArgumentException($"Not a slash command: '{command}'", nameof(command));

        var bytes = Encoding.UTF8.GetBytes(command);
        if (bytes.Length == 0 || bytes.Length > 500)
            throw new ArgumentException("Command is empty or longer than 500 bytes.", nameof(command));

        var mes = Utf8String.FromSequence(bytes);
        UIModule.Instance()->ProcessChatBoxEntry(mes);
        mes->Dtor(true);
    }
}
