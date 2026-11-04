# Chat window radio wrap (prefix and postfix)
# VG-Tweak Start
# The job title is rendered as an icon in radio messages instead of text.
chat-radio-message-wrap = [color={ $color }]{ $channel } [bold]{ $jobIcon }{ $name }[/bold] { $verb }, [font={ $fontType } size={ $fontSize }]{ chat-manager-speech-double-quote-begin }{ $message }{ chat-manager-speech-double-quote-end }[/font][/color]
chat-radio-message-wrap-bold = [color={ $color }]{ $channel } [bold]{ $jobIcon }{ $name }[/bold] { $verb }, [font={ $fontType } size={ $fontSize }][bold]{ chat-manager-speech-double-quote-begin }{ $message }{ chat-manager-speech-double-quote-end }[/bold][/font][/color]
# VG-Tweak End

examine-headset-default-channel = Use {$prefix} for the default channel ([color={$color}]{$channel}[/color]).

chat-radio-common = Common
chat-radio-centcom = CentComm
chat-radio-command = Command
chat-radio-engineering = Engineering
chat-radio-medical = Medical
chat-radio-science = Science
chat-radio-security = Security
chat-radio-service = Service
chat-radio-supply = Supply
chat-radio-syndicate = Syndicate
chat-radio-freelance = Freelance

# not headset but whatever
chat-radio-handheld = Handheld
chat-radio-binary = Binary
chat-radio-xenoborg = Xenoborg
chat-radio-mothership = Mothership
