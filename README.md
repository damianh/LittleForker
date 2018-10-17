# Little Forker [![Build Status](https://travis-ci.org/damianh/LittleForker.svg?branch=master)](https://travis-ci.org/damianh/LittleForker)

A utility to aid in the launching and supervision of processes. The primary use
case is installing a single service who then spawns other processes as part of a
multi-process application.

A package is available on my personal CI feed:
https://www.myget.org/F/dh/api/v3/index.json 

## Features

  1. Define and start a process passing in args and environment variables.
  2. Track the state of the process via state machine.
  3. Attach and capture a process's standard output.
  4. A named piped based IPC mechanism for the supervisor to signal an `EXIT` to a
     process to allow for cooperative shutdown.

## Areas of Interest

 1. [`ProcessSupervisor`](src/LittleForker/ProcessSupervisor.cs) is responsible
    for launching a process and montoring for exit. It handles two types of
    processes:
    1. **Self Terminating** where the process will exit of it's own accord.
    2. **Non Terminating** where the process must be signaled to shutdown _or_ be
       killed.
 2. [`ProcessMonitor`](src/LittleForker/ProcessMonitor.cs) is responsible for
    montoring a process and raising en event when it exits. This allows "child"
    processes to self-terminate cleanly.
 3. [`CooperativeShutdown`](src/LittleForker/CooperativeShutdown.cs) is
    responsible for allowing a process to receive a signal to exit over a named
    pipe and thus faciliting a clean shutdown. 
 4. Using `ProcessMonitor` and `CooperativeShutdown` in an application:
    https://github.com/damianh/LittleForker/blob/master/src/NonTerminatingProcess/Program.cs#L28-L30
 5. Explore [the tests](src/LittleForker.Tests/) to see how it works.

## Process State Machine

![statemachine](state-machine.png)

## Building

- Requires .NET Core 2.0 SDK or later
- Run `build.cmd` to compile, run tests and build package.

## Ideas

 1. Provide strategy pattern extension point to allow finer control over
    considering whether a process can be considered started. This can be the
    fact the process is running or something more complicated such as an HTTP
    health endpoint check.

## Credits

Hat tip to [@markrendle](https://twitter.com/markrendle) for the project name.
