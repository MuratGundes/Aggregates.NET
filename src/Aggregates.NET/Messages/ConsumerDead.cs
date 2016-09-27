﻿using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Messages
{
    public interface ConsumerDead : IEvent
    {
        String Endpoint { get; set; }
        Guid Instance { get; set; }
    }
}
